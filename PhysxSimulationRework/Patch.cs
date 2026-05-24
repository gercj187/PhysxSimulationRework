using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DV.Utils;
using DV.Simulation;
using DV.Simulation.Brake;
using DV.CabControls;
using DV.Simulation.Cars;
using DV.VFX;
//using DV.ModularAudioCar;
//using DV.Damage;

namespace PhysxSimulationRework
{
	[HarmonyPatch]
	internal static class TurntableTweaks
	{
		// -----------------------------
		// FieldRefs (private Felder)
		// -----------------------------
		private static readonly AccessTools.FieldRef<TurntableController, LeverBase> _leverControl =
			AccessTools.FieldRefAccess<TurntableController, LeverBase>("leverControl");

		private static readonly AccessTools.FieldRef<TurntableController, bool> _snappingAngleSet =
			AccessTools.FieldRefAccess<TurntableController, bool>("snappingAngleSet");

		private static readonly AccessTools.FieldRef<TurntableController, float> _snappingAngle =
			AccessTools.FieldRefAccess<TurntableController, float>("snappingAngle");

		private static readonly AccessTools.FieldRef<TurntableController, float> _snappingDirection =
			AccessTools.FieldRefAccess<TurntableController, float>("snappingDirection");

		private static readonly AccessTools.FieldRef<TurntableController, bool> _playTrackConnectedSound =
			AccessTools.FieldRefAccess<TurntableController, bool>("playTrackConnectedSound");

		private static readonly AccessTools.FieldRef<TurntableController, bool> _playSnapRangeEnterSound =
			AccessTools.FieldRefAccess<TurntableController, bool>("playSnapRangeEnterSound");

		private static readonly AccessTools.FieldRef<TurntableController, float> _lastSnappingAnglePlayed =
			AccessTools.FieldRefAccess<TurntableController, float>("lastSnappingAnglePlayed");

		private static readonly AccessTools.FieldRef<TurntableController, float> _rotationSoundIntensity =
			AccessTools.FieldRefAccess<TurntableController, float>("rotationSoundIntensity");

		private static readonly AccessTools.FieldRef<TurntableController, LayerMask> _playerLayerMask =
			AccessTools.FieldRefAccess<TurntableController, LayerMask>("playerLayerMask");

		private static readonly AccessTools.FieldRef<TurntableController, Collider[]> _playerOverlapResults =
			AccessTools.FieldRefAccess<TurntableController, Collider[]>("playerOverlapResults");

		private static readonly AccessTools.FieldRef<TurntableController, float> _pushingPositiveDirectionValue =
			AccessTools.FieldRefAccess<TurntableController, float>("pushingPositiveDirectionValue");

		private static readonly AccessTools.FieldRef<TurntableController, float> _pushingNegativeDirectionValue =
			AccessTools.FieldRefAccess<TurntableController, float>("pushingNegativeDirectionValue");

		private static readonly MethodInfo _updateSnappingRangeSound =
			AccessTools.Method(typeof(TurntableController), "UpdateSnappingRangeSound", new[] { typeof(float) });
			
		private static readonly Dictionary<TurntableController, float> _snapBrakeClamp = new();
		
		private static readonly HashSet<TurntableController> _lastWasPush = new();

		private static readonly Dictionary<TurntableController, float> _origSpeedMult = new();

		private static readonly Dictionary<TurntableController, int> _lastDriveDir = new();
		
		private static readonly Dictionary<TurntableController, float> _lastAngle = new();
		
		// -----------------------------
		// Turntable Bell Sound
		// -----------------------------
		private static readonly Dictionary<TurntableController, float> _nextBellTime = new();
		private static AudioClip? _bellClip;
		private static AudioMixerGroup? _bellMixer;
		private static readonly Dictionary<TurntableController, AudioSource> _bellSources = new();
		private static bool _bellLoadAttempted;
		
		private static TurntableWarningSound GetSelectedBell()
		{
			return Main.Settings?.turntableWarningSound	?? TurntableWarningSound.DashWarningBell;
		}
		
		private static void LoadBellFromResources()
		{
			if (_bellClip != null)
				return;

			_bellClip = Resources.Load<AudioClip>("DashWarning_01_Bell");

			if (_bellClip != null)
			{
				ModLog.Turntable("Loaded DashWarning_01_Bell from Resources");
			}
			else
			{
				ModLog.Turntable("DashWarning_01_Bell NOT found in Resources");
			}
		}
		
		private static bool IsActuallyMoving(TurntableController tc)
		{
			var tt = tc.turntable;
			if (tt == null)
				return false;

			float current = tt.currentYRotation;

			bool moving = false;

			if (_lastAngle.TryGetValue(tc, out float last))
			{
				float delta = Mathf.Abs(
					TurntableRailTrack.AngleRangeNeg180To180(current - last)
				);

				moving = delta > 0.001f;
			}

			_lastAngle[tc] = current;
			return moving;
		}
		
		private static void EnsureBellLoaded()
		{
			if (_bellClip != null || _bellLoadAttempted)
				return;

			_bellLoadAttempted = true;

			var selected = GetSelectedBell();

			// =====================================================
			// CLASSIC BELL
			// =====================================================
			if (selected == TurntableWarningSound.DashWarningBell)
			{
				var lamps = Resources.FindObjectsOfTypeAll<LampControl>();
				foreach (var lamp in lamps)
				{
					if (lamp?.warningAudio == null)
						continue;

					if (lamp.warningAudio.name == "DashWarning_01_Bell")
					{
						_bellClip = lamp.warningAudio;
						_bellMixer = lamp.lampAudioMixerGroup;

						ModLog.Turntable("Using DashWarning bell (LampControl)");
						return;
					}
				}

				ModLog.Turntable("DashWarning bell NOT found");
				return;
			}

			// =====================================================
			// DH4 HORN
			// =====================================================
			if (selected == TurntableWarningSound.TrainBell_DH4)
			{
				var sources = Resources.FindObjectsOfTypeAll<AudioSource>();
				foreach (var src in sources)
				{
					if (src?.clip == null)
						continue;

					if (src.clip.name == "Train_Bell-DH4_01")
					{
						_bellClip = src.clip;
						_bellMixer = src.outputAudioMixerGroup;

						ModLog.Turntable("Using DH4 horn from AudioSource");
						return;
					}
				}

				ModLog.Turntable("DH4 horn not available → fallback to DashWarning");
				_bellLoadAttempted = false;
				if (Main.Settings != null)
				{
					Main.Settings.turntableWarningSound = TurntableWarningSound.DashWarningBell;
				}
				EnsureBellLoaded();
			}
		}
		
		private static AudioSource GetOrCreateBellSource(TurntableController tc)
		{
			if (_bellSources.TryGetValue(tc, out var src) && src != null)
				return src;

			var go = new GameObject("TurntableBell_Audio");

			var railTrack = tc.turntable;
			if (railTrack != null)
				go.transform.SetParent(railTrack.transform, false);
			else
				go.transform.SetParent(tc.transform, false);

			src = go.AddComponent<AudioSource>();
			src.clip = _bellClip;

			bool isBell =
				Main.Settings?.turntableWarningSound == TurntableWarningSound.DashWarningBell;

			src.volume = isBell ? 3.0f : 1.0f;
			src.pitch = 1f;
			src.spatialBlend = 1f;
			src.minDistance = isBell ? 3f : 1f;
			src.maxDistance = isBell ? 80f : 150f;
			src.rolloffMode = AudioRolloffMode.Logarithmic;

			if (_bellMixer != null && !isBell)
			{
				src.outputAudioMixerGroup = _bellMixer;
			}

			src.playOnAwake = false;
			src.loop = false;

			_bellSources[tc] = src;
			return src;
		}

		
		internal static void NotifyBellSoundChanged()
		{
			_bellClip = null;
			_bellMixer = null;
			_bellLoadAttempted = false;

			foreach (var src in _bellSources.Values)
			{
				if (src != null)
					UnityEngine.Object.Destroy(src.gameObject);
			}
			_bellSources.Clear();

			ModLog.Turntable("Turntable bell sound changed");
		}
		
		private static void PlayBell(TurntableController tc)
		{
			ModLog.Turntable("PlayBell() called");
			
			EnsureBellLoaded();
			
			if (_bellClip == null)
			{
				ModLog.Turntable("_bellClip is NULL");
				return;
			}

			if (_bellMixer == null)
			{
				ModLog.Turntable("_bellMixer is NULL");
			}
			else
			{
				ModLog.Turntable("Mixer OK: " + _bellMixer.name);
			}
			
			if (tc == null || _bellClip == null)
				return;

			var src = GetOrCreateBellSource(tc);
			
			if (src.clip != _bellClip)
			{
				src.clip = _bellClip;
			}
			ModLog.Turntable(
				$"AudioSource state: " +
				$"isPlaying={src.isPlaying} " +
				$"enabled={src.enabled} " +
				$"vol={src.volume} " +
				$"spatial={src.spatialBlend} " +
				$"pos={src.transform.position}"
			);
			if (!src.isPlaying)
			{
				src.Play();
				ModLog.Turntable("AudioSource.Play() called");
			}
		}
		
		private static void HandleBell(TurntableController tc)
		{
			var settings = Main.Settings;
			if (settings == null)
				return;

			bool isMoving = IsActuallyMoving(tc);

			bool blockBecausePush =
				!settings.enablePushToDetect
				&& _lastWasPush.Contains(tc);

			bool bellAllowed =
				settings.enableTurntableTweaks
				&& isMoving
				&& !blockBecausePush;

			if (!bellAllowed)
			{
				_nextBellTime.Remove(tc);
				return;
			}

			if (!_nextBellTime.TryGetValue(tc, out float next))
				next = 0f;

			if (Time.time >= next)
			{
				PlayBell(tc);
				_nextBellTime[tc] = Time.time + 3f;
			}
		}

		[HarmonyTargetMethod]
		private static MethodBase TargetMethod()
			=> AccessTools.Method(typeof(TurntableController), "FixedUpdate");

		[HarmonyPrefix]
		private static bool FixedUpdate_Prefix(TurntableController __instance)
		{
			if (!__instance.PlayerControlAllowed)
			{
				_rotationSoundIntensity(__instance) = 0f;
			}

			var settings = Main.Settings;
			if (settings == null || !settings.enableTurntableTweaks)
			{
				RestoreSpeedMultiplier(__instance);
				return true;
			}

			CacheAndApplySpeedMultiplier(__instance);

			if (!WorldStreamingInit.IsLoaded)
				return false;

			var turntable = __instance.turntable;
			if (turntable == null)
				return false;

			var lever = _leverControl(__instance);
			if (lever == null)
				return false;

			float value = (__instance.PlayerControlAllowed ? lever.Value : 0.5f);

			float pushPos = _pushingPositiveDirectionValue(__instance);
			float pushNeg = _pushingNegativeDirectionValue(__instance);

			float posInput = (pushPos != 0f) ? pushPos : Mathf.InverseLerp(0.55f, 1f, value);
			if (posInput > 0f)
			{
				_lastDriveDir[__instance] = +1;
				
				if (pushPos != 0f)
					_lastWasPush.Add(__instance);
				else
					_lastWasPush.Remove(__instance);
				
				_rotationSoundIntensity(__instance) = posInput;
				
				_snappingAngleSet(__instance) = false;

				CallUpdateSnappingRangeSound(__instance, turntable.ClosestSnappingAngle());

				float degPerSec = posInput * 12f * __instance.speedMultiplier;
				turntable.targetYRotation = TurntableRailTrack.AngleRange0To360(
					turntable.targetYRotation + degPerSec * Time.fixedDeltaTime
				);
				turntable.RotateToTargetRotation();
				HandleBell(__instance);
				return false;
			}

			float negInput = (pushNeg != 0f) ? pushNeg : Mathf.InverseLerp(0.45f, 0f, value);
			if (negInput > 0f)
			{
				_lastDriveDir[__instance] = -1;
				
				if (pushNeg != 0f)
					_lastWasPush.Add(__instance);
				else
					_lastWasPush.Remove(__instance);

				_rotationSoundIntensity(__instance) = negInput;

				_snappingAngleSet(__instance) = false;

				CallUpdateSnappingRangeSound(__instance, turntable.ClosestSnappingAngle());

				float degPerSec = (-negInput) * 12f * __instance.speedMultiplier;
				turntable.targetYRotation = TurntableRailTrack.AngleRange0To360(
					turntable.targetYRotation + degPerSec * Time.fixedDeltaTime
				);
				turntable.RotateToTargetRotation();
				HandleBell(__instance);
				return false;
			}

			if (!_snappingAngleSet(__instance))
			{
				if (_lastWasPush.Remove(__instance))
				{
					if (!(settings.enablePushToDetect && settings.snapAngleToleranceDeg > 0f))
					{
						_rotationSoundIntensity(__instance) = 0f;
						_snappingAngle(__instance) = -1f;
						_snappingAngleSet(__instance) = true;
						return false;
					}
				}
				
				int dir = _lastDriveDir.TryGetValue(__instance, out int d) ? d : 0;

				float tolerance = Mathf.Clamp(settings.snapAngleToleranceDeg, 0.0f, 180f);

				if (tolerance <= 0f)
				{
					_snappingAngle(__instance) = -1f;
					_snappingAngleSet(__instance) = true;
					return false;
				}
				
				float foundAngle;
				float foundDir;
				bool found = TryFindSnappingAngleDirectional(turntable, dir, tolerance, out foundAngle, out foundDir);				

				if (found)
				{
					_snappingAngle(__instance) = foundAngle;
					_snappingDirection(__instance) = foundDir;
				}
				else
				{
					_snappingAngle(__instance) = -1f;
				}

				_snappingAngleSet(__instance) = true;
				_snapBrakeClamp[__instance] = 1f;
			}

			float snapAngle = _snappingAngle(__instance);
			if (snapAngle >= 0f)
			{
				float currentY = turntable.currentYRotation;
				float angleA = TurntableRailTrack.AngleRange0To360(currentY + 180f);

				if (!TurntableRailTrack.AnglesEqual(currentY, snapAngle) && !TurntableRailTrack.AnglesEqual(angleA, snapAngle))
				{
					float num6 = TurntableRailTrack.AngleRangeNeg180To180(snapAngle - turntable.targetYRotation);
					float num7 = TurntableRailTrack.AngleRangeNeg180To180(num6 + 180f);
					float f3 = (Mathf.Abs(num6) < Mathf.Abs(num7)) ? num6 : num7;

					float snapDir = _snappingDirection(__instance);

					float rotationMult = Main.Settings?.turntableRotationSpeedMultiplier ?? 1f;
					float snapMult = Mathf.Max(0.1f, rotationMult - 0.1f);

					float snapSpeed = 10f * snapMult;

					float remainingDeg = Mathf.Abs(f3);

					float brakeFactorTarget = 1f;

					if (remainingDeg <= 5f) brakeFactorTarget = 0.80f;
					if (remainingDeg <= 4f) brakeFactorTarget = 0.70f;
					if (remainingDeg <= 3f) brakeFactorTarget = 0.60f;
					if (remainingDeg <= 2f) brakeFactorTarget = 0.50f;
					if (remainingDeg <= 1.5f) brakeFactorTarget = 0.40f;
					if (remainingDeg <= 1f) brakeFactorTarget = 0.30f;
					if (remainingDeg <= 0.7f) brakeFactorTarget = 0.15f;
					if (remainingDeg <= 0.4f) brakeFactorTarget = 0.05f;

					float brakeFactor = brakeFactorTarget;

					if (_snapBrakeClamp.TryGetValue(__instance, out float prev))
					{
						brakeFactor = Mathf.Min(prev, brakeFactorTarget);
					}

					_snapBrakeClamp[__instance] = brakeFactor;

					float maxStep = snapSpeed * brakeFactor * Time.fixedDeltaTime;

					float num8 = snapDir * Mathf.Min(remainingDeg, maxStep);

					_rotationSoundIntensity(__instance) = Mathf.Max(_rotationSoundIntensity(__instance), 0.25f);
					
					turntable.targetYRotation = TurntableRailTrack.AngleRange0To360( turntable.targetYRotation + num8);

					turntable.RotateToTargetRotation();
					
					HandleBell(__instance);
				}
				else
				{
					_playTrackConnectedSound(__instance) = true;
					_snappingAngle(__instance) = -1f;
					
					_rotationSoundIntensity(__instance) = 0f;
					_snapBrakeClamp.Remove(__instance);
				}
			}
			return false;
		}

		private static bool TryFindSnappingAngleDirectional(TurntableRailTrack tt,int direction,float toleranceDeg,out float bestAngle,out float bestDir)
		{
			bestAngle = -1f;
			bestDir = 0f;

			if (tt == null || tt.trackEnds == null || tt.trackEnds.Count == 0)
				return false;

			if (direction != 1 && direction != -1)
				return false;

			float front = TurntableRailTrack.AngleRange0To360(tt.currentYRotation);
			float rear  = TurntableRailTrack.AngleRange0To360(tt.currentYRotation + 180f);

			float bestDelta = float.MaxValue;

			for (int i = 0; i < tt.trackEnds.Count; i++)
			{
				float teAngle = TurntableRailTrack.AngleRange0To360(tt.trackEnds[i].angle);

				// -----------------------------
				// FRONT side
				// -----------------------------
				float deltaFront = TurntableRailTrack.AngleRange0To360(teAngle - front);

				if (direction == -1)
					deltaFront = 360f - deltaFront;

				if (deltaFront > 0f && deltaFront <= toleranceDeg)
				{
					if (deltaFront < bestDelta)
					{
						bestDelta = deltaFront;
						bestAngle = teAngle;
						bestDir = direction;
					}
				}

				// -----------------------------
				// REAR side (180° versetzt)
				// -----------------------------
				float deltaRear = TurntableRailTrack.AngleRange0To360(teAngle - rear);

				if (direction == -1)
					deltaRear = 360f - deltaRear;

				if (deltaRear > 0f && deltaRear <= toleranceDeg)
				{
					if (deltaRear < bestDelta)
					{
						bestDelta = deltaRear;
						bestAngle = teAngle;
						bestDir = direction;
					}
				}
			}

			return bestAngle >= 0f;
		}

		private static void CallUpdateSnappingRangeSound(TurntableController tc, float currentSnappingAngle)
		{
			if (tc == null)
				return;

			if (_updateSnappingRangeSound == null)
				return;

			try
			{
				_updateSnappingRangeSound.Invoke(tc, new object[] { currentSnappingAngle });
			}
			catch
			{
			}
		}

		private static void CacheAndApplySpeedMultiplier(TurntableController tc)
		{
			if (!_origSpeedMult.TryGetValue(tc, out float original))
			{
				original = tc.speedMultiplier;
				_origSpeedMult[tc] = original;
			}

			float mult = Main.Settings?.turntableRotationSpeedMultiplier ?? 1f;
			tc.speedMultiplier = original * mult;
		}

		private static void RestoreSpeedMultiplier(TurntableController tc)
		{
			if (tc == null) return;
			if (_origSpeedMult.TryGetValue(tc, out float original))
				tc.speedMultiplier = original;
		}
	}

	// -----------------------------
	// AirbrakeCock
	// -----------------------------	
    [HarmonyPatch]
    internal static class HoseAndCock_AsymmetricVenting_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method("DV.Simulation.Brake.HoseAndCock:get_IsOpenToAtmosphere");
        }

        static void Postfix(object __instance, ref bool __result)
        {
            if (__instance == null)
                return;

            var settings = Main.Settings;
            if (settings == null || !settings.enableAsymmetricCockVenting)
                return;

            dynamic hose = __instance;

            dynamic other = hose.connectedTo;
            if (other == null)
                return;
			
            bool thisOpen = hose.cockOpen;
            bool otherOpen = other.cockOpen;

            if (thisOpen == otherOpen)
                return;
			
            if (thisOpen && !otherOpen)
            {
                __result = true;
            }
        }
    }

	// -----------------------------
	// Couplefails - DERAIL
	// -----------------------------

	[HarmonyPatch(typeof(TrainCar), "Derail")]
	public static class TrainCar_Derail_Patch
	{
		static void Postfix(TrainCar __instance)
		{
			var settings = Main.Settings;
			if (settings == null || !settings.enableCouplerFailure)
				return;

			if (__instance.couplers == null)
				return;

			float chance = settings.chanceToBreakOnDerail;
			if (chance <= 0f)
				return;

			foreach (var coupler in __instance.couplers)
			{
				if (coupler == null || !coupler.IsCoupled())
					continue;

				switch (coupler.state)
				{
					case ChainCouplerInteraction.State.Attached_Loose:
					case ChainCouplerInteraction.State.Attached_Tight:
						break;

					default:
						continue;
				}

				if (UnityEngine.Random.value > chance)
					continue;

				var cj = coupler.rigidCJ;
				if (cj == null)
					continue;

				cj.breakForce = 5000f;
				cj.breakTorque = 5000f;

				ModLog.Derail(
					$"[PhysxSimulationRework][PhysX] Coupler weakened by derailment " +
					$"| CarID={__instance.ID} | State={coupler.state}"
				);
			}
		}
	}

	[HarmonyPatch(typeof(Coupler), "CreateRigidJoint")]
	public static class Coupler_CreateRigidJoint_Patch
	{
		static void Postfix(Coupler __instance)
		{
			if (__instance == null || __instance.rigidCJ == null)
				return;

			if (!WeakCouplerRegistry.IsMarked(__instance))
				return;

			__instance.rigidCJ.breakForce = 1f;
			__instance.rigidCJ.breakTorque = 1f;

			ModLog.Derail(
				$"[PhysxSimulationRework] breakForce overridden AFTER CreateJoint"
			);
		}
	}

    // -----------------------------
	// Couplefails - FORCE BRAKE
	// -----------------------------

	public static class WeakCouplerRegistry
	{
		private static readonly HashSet<Coupler> marked = new HashSet<Coupler>();

		public static void Mark(Coupler coupler)
		{
			if (coupler != null)
				marked.Add(coupler);
		}

		public static bool IsMarked(Coupler coupler)
		{
			return coupler != null && marked.Contains(coupler);
		}
	}
		
	internal static class CouplerJointRegistry
	{
		public static readonly Dictionary<int, List<Coupler>> JointToCouplers = new();
		public static readonly HashSet<int> ArmedJoints = new();
		
		public static void Register(int jointId, Coupler coupler)
		{
			if (coupler == null) return;

			if (!JointToCouplers.TryGetValue(jointId, out var list) || list == null)
			{
				list = new List<Coupler>(2);
				JointToCouplers[jointId] = list;
			}

			for (int i = 0; i < list.Count; i++)
			{
				if (ReferenceEquals(list[i], coupler))
					return;
			}

			list.Add(coupler);
		}
	}
	
    internal class CouplerJointBreakListener : MonoBehaviour
	{
		private void OnJointBreak(float breakForce)
		{
			var joint = GetComponent<Joint>();
			
			if (joint == null)
			{
				ModLog.Coupler("OnJointBreak called but NO Joint component found");
				return;
			}

			int jointId = joint.GetInstanceID();
			
			if (!CouplerJointRegistry.JointToCouplers.ContainsKey(jointId))
			{
				ModLog.Coupler($"IGNORE joint break with NO registered couplers | id={jointId}");
				return;
			}

			ModLog.Coupler($"Joint BROKE | id={jointId} | force={breakForce:F1}");

			if (!CouplerJointRegistry.ArmedJoints.Contains(jointId))
			{
				ModLog.Coupler($"IGNORE broken joint (NOT ARMED) | id={jointId}");
				return;
			}

			CouplerJointRegistry.ArmedJoints.Remove(jointId);

			if (!CouplerJointRegistry.JointToCouplers.TryGetValue(jointId, out var couplers)
				|| couplers == null || couplers.Count == 0)
			{
				ModLog.Coupler($"No couplers registered for armed joint | id={jointId}");
				return;
			}

			for (int i = 0; i < couplers.Count; i++)
			{
				var c = couplers[i];
				if (c == null) continue;

				var car = c.GetComponentInParent<TrainCar>();
				string carName = car != null ? car.name : "<unknown>";
				int carId = car != null ? car.GetInstanceID() : -1;

				ModLog.Coupler(
					$"Joint {jointId} coupler[{i}] " +
					$"| car={carName}({carId}) " +
					$"| state={c.state} " +
					$"| coupled={c.IsCoupled()}"
				);
			}

	#pragma warning disable CS8600
			Coupler chosen = null;
	#pragma warning restore CS8600

			for (int i = 0; i < couplers.Count; i++)
			{
				var c = couplers[i];
				if (c == null) continue;
				if (!c.IsCoupled()) continue;

				if (ReferenceEquals(c.rigidCJ, joint))
				{
					chosen = c;
					break;
				}
			}

			if (chosen == null)
			{
				for (int i = 0; i < couplers.Count; i++)
				{
					var c = couplers[i];
					if (c == null) continue;
					if (!c.IsCoupled()) continue;

					if (c.state == ChainCouplerInteraction.State.Attached_Loose)
					{
						chosen = c;
						break;
					}
				}
			}

			if (chosen == null)
			{
				for (int i = 0; i < couplers.Count; i++)
				{
					var c = couplers[i];
					if (c == null) continue;
					if (!c.IsCoupled()) continue;

					chosen = c;
					break;
				}
			}
		}
	}

	[HarmonyPatch(typeof(DrivingForce), "FixedUpdate")]
	public static class DrivingForce_StressTrigger_Patch
	{
		private static readonly HashSet<Coupler> breakUnlocked = new();

		public static readonly Dictionary<Coupler, float> VanillaBreakForce = new();
		public static readonly Dictionary<Coupler, float> VanillaBreakTorque = new();
		
		internal static void ClearLoggedCars()
		{
			loggedCars.Clear();
		}

		private static readonly HashSet<string> loggedCars = new();		

		static void Postfix(DrivingForce __instance)
		{
			var settings = Main.Settings;
			if (settings == null || !settings.enableCouplerFailure)
				return;

			var train = AccessTools.Field(typeof(DrivingForce), "train")
				?.GetValue(__instance) as TrainCar;

			if (train == null)
				return;

			float tension = Mathf.Abs(__instance.generatedForce);

			var ts = train.trainset;
			if (ts == null || ts.cars == null)
				return;

			foreach (var car in ts.cars)
			{
				if (car == null)
					continue;

				if (car.couplers == null)
					continue;

				foreach (var coupler in car.couplers)
				{
					if (coupler == null || !coupler.IsCoupled())
						continue;

					var cj = coupler.rigidCJ;
					if (cj == null)
						continue;

					int jointId = cj.GetInstanceID();
					
					if (coupler.state == ChainCouplerInteraction.State.Parked)
						continue;

					if (jointId == 0)
						continue;

					CouplerJointRegistry.Register(jointId, coupler);

					var go = cj.gameObject;
					if (go.GetComponent<CouplerJointBreakListener>() == null)
					{
						go.AddComponent<CouplerJointBreakListener>();
						ModLog.Coupler($"BreakListener attached | jointId={jointId}");
					}

					if (!VanillaBreakForce.ContainsKey(coupler))
					{
						VanillaBreakForce[coupler] = cj.breakForce;
						VanillaBreakTorque[coupler] = cj.breakTorque;
					}

					if (settings.chanceToBreakOnStress <= 0f)
					{
						cj.breakForce = float.PositiveInfinity;
						cj.breakTorque = float.PositiveInfinity;

						breakUnlocked.Remove(coupler);
						continue;
					}

					switch (coupler.state)
					{
						case ChainCouplerInteraction.State.Attached_Loose:
							break;

						case ChainCouplerInteraction.State.Attached_Tight:
						case ChainCouplerInteraction.State.Attached_Tightening_Couple:
						case ChainCouplerInteraction.State.Attached_Loosening_Uncouple:
						case ChainCouplerInteraction.State.Determine_Next_State:
						default:
							cj.breakForce = float.PositiveInfinity;
							cj.breakTorque = float.PositiveInfinity;

							breakUnlocked.Remove(coupler);
							continue;
					}

					bool armed = CouplerJointRegistry.ArmedJoints.Contains(jointId);

					if (!armed)
					{
						if (VanillaBreakForce.TryGetValue(coupler, out var bf))
							cj.breakForce = bf;
						else
							cj.breakForce = float.PositiveInfinity;

						if (VanillaBreakTorque.TryGetValue(coupler, out var bt))
							cj.breakTorque = bt;
						else
							cj.breakTorque = float.PositiveInfinity;

						continue;
					}

					cj.breakForce = settings.customBreakForce;
					cj.breakTorque = settings.customBreakForce;
				}
			}
		}
		
		internal static void RollStressChanceForJoint(int jointId, Coupler anyCouplerForLog)
		{
			var settings = Main.Settings;
			if (settings == null)
				return;

			CouplerJointRegistry.ArmedJoints.Remove(jointId);

			bool success = UnityEngine.Random.value <= settings.chanceToBreakOnStress;
			if (success)
				CouplerJointRegistry.ArmedJoints.Add(jointId);

			ModLog.Coupler(
				$"Stress roll {(success ? "SUCCESS" : "FAIL")} " +
				$"| jointId={jointId} | Coupler={anyCouplerForLog?.name}"
			);
		}


		// -------------------------------------------------
		// Helper: Loggt TrainCar.ID + beide Kupplungen
		// -------------------------------------------------
		internal static void LogCarCouplerJoints_Public(TrainCar car)
		{
			LogCarCouplerJoints(car);
		}
		
		private static void LogCarCouplerJoints(TrainCar car)
		{
			if (car == null)
				return;

			bool isLoco = car.IsLoco;

			string kind = isLoco ? "LOCO" : "CAR";
			string carId = car.ID;
			string carType = car.carLivery != null ? car.carLivery.id : "<unknown>";

			ModLog.Coupler(
				$"CarID={carId} ({kind}) Type={carType}\n" +
				"Registered jointIds:"
			);

			LogCoupler(car.frontCoupler, "FRONT");
			LogCoupler(car.rearCoupler, "REAR");
		}

		private static void LogCoupler(Coupler coupler, string label)
		{
			if (coupler == null)
			{
				ModLog.Coupler($"- jointId=<null> | coupler={label} | state=<null>");
				return;
			}

			var cj = coupler.rigidCJ;
			string state = coupler.state.ToString();

			if (cj == null)
			{
				ModLog.Coupler($"- jointId=<none> | coupler={label} | state={state}");
				return;
			}

			ModLog.Coupler($"- jointId={cj.GetInstanceID()} | coupler={label} | state={state}");
		}
	}

	// -------------------------------------------------
	// DEBUG: Trainset betreten
	// -------------------------------------------------	
	[HarmonyPatch(typeof(PlayerManager), "set_Car")]
	public static class PlayerManager_SetCar_ConsistLog_Patch
	{
		static void Postfix(TrainCar __0)
		{
			var car = __0;
			if (car == null)
				return;

			var ts = car.trainset;
			if (ts == null || ts.cars == null)
				return;

			int locoCount = 0;
			int tenderCount = 0; 
			int carCount = 0;

			List<string> lines = new();
			int index = 1;

			foreach (var c in ts.cars)
			{
				if (c == null)
					continue;

				string type   = c.carLivery != null ? c.carLivery.id : "<unknown>";
				string number = c.ID;

				if (c.IsLoco)
				{
					locoCount++;
				}
				else
				{
					carCount++;
				}

				lines.Add($"\t#{index} {type} | Number= {number}");
				index++;
			}

			ModLog.Coupler(
				$"Refresh train state:\n" +
				$"\tLocos= {locoCount} | Tenders= {tenderCount} | Cars= {carCount}\n" +
				string.Join("\n", lines)
			);

			DrivingForce_StressTrigger_Patch.ClearLoggedCars();
			
			foreach (var c in ts.cars)
			{
				if (c == null || c.couplers == null)
					continue;
				
				DrivingForce_StressTrigger_Patch.LogCarCouplerJoints_Public(c);

				var rolledJoints = new HashSet<int>();

				foreach (var coupler in c.couplers)
				{
					if (coupler == null || !coupler.IsCoupled())
						continue;

					var cj = coupler.rigidCJ;
					if (cj == null)
						continue;

					int jointId = cj.GetInstanceID();
					if (jointId == 0)
						continue;

					if (!rolledJoints.Add(jointId))
						continue;

					DrivingForce_StressTrigger_Patch.RollStressChanceForJoint(jointId, coupler);
				}
			}
		}
	}	
	
	// ======================================================
	// DYNAMIC DERAIL RISK
	// ======================================================

	[HarmonyPatch(typeof(Bogie), "FixedUpdate")]
	internal static class DynamicDerailRisk_Patch
	{
		private static readonly Dictionary<TrainCar, float> timers = new();
		private static readonly Dictionary<TrainCar, float> riskLevel = new();

		// ======================================================
		// CORE CALCULATION
		// ======================================================
		private static float CalculateNewDerailChance(
			float damagePercent,
			float speedKmh,
			bool exploded,
			PhysxSimulationReworkSettings settings
		)
		{
			float condition = 100f - damagePercent;
			float safeSpeed = Mathf.Max(settings.baseSafeSpeed, condition);

			if (speedKmh <= safeSpeed)
				return 0f;

			float overSpeed = speedKmh - safeSpeed;

			float speedFactor = speedKmh * 0.1f;

			float tier = Mathf.Floor(overSpeed / 10f);
			float tierMultiplier = 0.1f + (tier * 0.1f);

			float safeFactor = overSpeed * tierMultiplier;

			float baseDamage = damagePercent / 100f;

			float damageScale = 1f;

			switch (settings.damageScaling)
			{
				case DamageScalingMode.Half:
					damageScale = 0.5f;
					break;

				case DamageScalingMode.Normal:
					damageScale = 1f;
					break;

				case DamageScalingMode.High:
					damageScale = 1.5f;
					break;
			}

			float damageFactor = baseDamage * damageScale;

			if (exploded)
				damageFactor *= 2f;

			float chance = (speedFactor + safeFactor) * damageFactor;

			return Mathf.Clamp(chance / 100f, 0f, 1f);
		}

		// ======================================================
		// MAIN LOOP
		// ======================================================
		static void Postfix(Bogie __instance)
		{
			var settings = Main.Settings;
			if (settings == null || !settings.enableDynamicDerailRisk)
				return;

			if (__instance == null || __instance.Car == null)
				return;

			if (!__instance.isFrontBogie)
				return;

			TrainCar car = __instance.Car;

			if (car.derailed || __instance.rb == null)
				return;

			// =========================
			// TIMER PRO WAGEN
			// =========================
			if (!timers.ContainsKey(car))
				timers[car] = 0f;

			timers[car] += Time.fixedDeltaTime;

			if (timers[car] < settings.derailInterval)
				return;

			timers[car] = 0f;

			// =========================
			// DATEN
			// =========================
			float speedKmh = __instance.rb.velocity.magnitude * 3.6f;
			
			float damagePercent = car.CarDamage != null
				? car.CarDamage.DamagePercentage * 100f
				: 0f;
				
			if (speedKmh <= 1f || damagePercent <= 0f)
			return;

			bool exploded = car.CarDamage != null && car.CarDamage.DamagePercentage >= 1f;

			string carId = car.ID;
			string carType = car.carLivery != null ? car.carLivery.id : "UNKNOWN";

			float chance = CalculateNewDerailChance(
				damagePercent,
				speedKmh,
				exploded,
				settings
			);

			// =========================
			// RISK SYSTEM
			// =========================
			if (!riskLevel.ContainsKey(car))
				riskLevel[car] = 0f;

			bool success = UnityEngine.Random.value < chance;

			if (success)
			{
				riskLevel[car] += settings.riskIncreasePerHit;

				ModLog.Risk(
					$"SUCCESS | Car={carId} ({carType}) | Risk={riskLevel[car]:F2} | Chance={chance * 100f:F1}% | Speed={speedKmh:F0} | Damage={damagePercent:F0}"
				);
			}
			else
			{
				riskLevel[car] -= settings.riskDecreaseOnFail;
				riskLevel[car] = Mathf.Max(0f, riskLevel[car]);

				ModLog.Risk(
					$"FAIL | Car={carId} ({carType}) | Risk={riskLevel[car]:F2} | Chance={chance * 100f:F1}% | Speed={speedKmh:F0} | Damage={damagePercent:F0}"
				);
			}

			// =========================
			// DERAIL TRIGGER
			// =========================
			if (riskLevel[car] >= settings.riskThreshold)
			{
				ModLog.Risk(
					$"DERAIL TRIGGERED | Car={carId} ({carType}) | Risk={riskLevel[car]:F2} | Speed={speedKmh:F0}"
				);

				riskLevel[car] = 0f;

				__instance.Derail("Dynamic derail system");
			}
		}
	}
	
	[HarmonyPatch(typeof(DerailedParticles), "OnCollision")]
    public static class DerailedParticles_OnCollision_Patch
    {
        static void Prefix(object __instance, Collision collision, bool becausePause)
        {
            if (__instance == null || collision == null || becausePause)
                return;

            TrainCar car = (TrainCar)typeof(DerailedParticles)
                .GetField("car", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(__instance);

            if (car == null)
                return;

            if (!car.derailed)
                return;

            Collider col = collision.collider;
            if (col == null)
                return;

            bool isTerrain = col.gameObject.layer == LayerMask.NameToLayer("Terrain");

            bool isGravel = IsGravelCollider(col);

            if (!isTerrain && !isGravel)
                return;

            CallDoDrag(__instance, collision);
            CallDoImpact(__instance, collision);
        }

        private static bool IsGravelCollider(Collider col)
        {
            string path = GetFullPath(col.transform);
            return path.Contains("Near_Colliders_Gravel");
        }

        private static void CallDoDrag(object instance, Collision collision)
        {
            MethodInfo method = typeof(DerailedParticles)
                .GetMethod("DoDrag", BindingFlags.NonPublic | BindingFlags.Instance);

            method?.Invoke(instance, new object[] { collision });
        }

        private static void CallDoImpact(object instance, Collision collision)
        {
            MethodInfo method = typeof(DerailedParticles)
                .GetMethod("DoImpact", BindingFlags.NonPublic | BindingFlags.Instance);

            method?.Invoke(instance, new object[] { collision });
        }

        private static string GetFullPath(Transform t)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            while (t != null)
            {
                if (sb.Length == 0)
                    sb.Insert(0, t.name);
                else
                    sb.Insert(0, t.name + "/");

                t = t.parent;
            }

            return sb.ToString();
        }
    }

	// ======================================================
	// BRAKE OVERHEATING DAMAGE
	// ======================================================

	[HarmonyPatch(typeof(BrakesOverheatingController), "Start")]
	public static class BrakesOverheatingController_Start_Patch
	{
		static void Postfix(BrakesOverheatingController __instance)
		{
			if (__instance == null)
				return;

			var car = TrainCar.Resolve(__instance.gameObject);

			if (car == null)
				return;

			if (car.GetComponent<OverheatDamageBehaviour>() != null)
				return;

			car.gameObject.AddComponent<OverheatDamageBehaviour>();

			if (car.logicCar != null)
			{
				ModLog.Overheat(
					$"[OverheatDamage] Attached to {car.ID}"
				);
			}
		}
	}
	
	// ======================================================
	// OVERHEAT DAMAGE BEHAVIOUR
	// ======================================================

	public class OverheatDamageBehaviour : MonoBehaviour
	{
		private TrainCar? car;
		private float timer;
		private object? heatController;
		private FieldInfo? temperatureField;

		private void Awake()
		{
			car = GetComponent<TrainCar>();

			if (car == null)
				return;

			try
			{
				// ------------------------------------------
				// brakeSystem
				// ------------------------------------------

				var brakeSystemField = typeof(TrainCar)
					.GetField(
						"brakeSystem",
						BindingFlags.Public |
						BindingFlags.NonPublic |
						BindingFlags.Instance
					);

				if (brakeSystemField == null)
				{
					Debug.LogError(
						"[OverheatDamage] brakeSystem field NOT FOUND"
					);
					return;
				}

				object brakeSystem =
					brakeSystemField.GetValue(car);

				if (brakeSystem == null)
					return;

				// ------------------------------------------
				// heatController
				// ------------------------------------------

				var hcField = brakeSystem.GetType()
					.GetField(
						"heatController",
						BindingFlags.Public |
						BindingFlags.NonPublic |
						BindingFlags.Instance
					);

				if (hcField == null)
				{
					Debug.LogError("[OverheatDamage] heatController field NOT FOUND");
					return;
				}

				heatController = hcField.GetValue(brakeSystem);

				if (heatController == null)
					return;

				// ------------------------------------------
				// temperature field
				// ------------------------------------------

				temperatureField = heatController.GetType()
					.GetField(
						"temperature",
						BindingFlags.NonPublic |
						BindingFlags.Public |
						BindingFlags.Instance
					);

				if (car.logicCar != null)
				{
					ModLog.Overheat(
						$"[OverheatDamage] Initialized for {car.ID}"
					);
				}
			}
			catch (Exception ex)
			{
				Debug.LogError(
					$"[OverheatDamage] Init failed: {ex}"
				);
			}
		}

		private void Update()
		{
			if (car == null)
				return;

			if (car.CarDamage == null)
				return;

			if (heatController == null)
				return;

			if (temperatureField == null)
			{
				Debug.LogError(
					"[OverheatDamage] temperature field NOT FOUND"
				);
				return;
			}

			float temp;

			try
			{
				temp = (float)temperatureField.GetValue(heatController);
			}
			catch
			{
				return;
			}

			// DEBUG
			if (Time.frameCount % 300 == 0 && temp >= 600f)
			{
				ModLog.Overheat(
					$"[OverheatDamage] {car.ID} temp={temp:F0}"
				);
			}

			// ------------------------------------------
			// UNDER 600
			// ------------------------------------------

			if (temp < 600f)
			{
				timer = 0f;
				return;
			}

			// ------------------------------------------
			// TIMER
			// ------------------------------------------

			timer += Time.deltaTime;

			float interval = 60f;
			float damage = 1f;

			if (temp >= 999f)
			{
				interval = 5f;
				damage = 20f;
			}
			else if (temp >= 900f)
			{
				interval = 10f;
				damage = 15f;
			}
			else if (temp >= 850f)
			{
				interval = 15f;
				damage = 10f;
			}
			else if (temp >= 800f)
			{
				interval = 20f;
				damage = 5f;
			}
			else if (temp >= 750f)
			{
				interval = 30f;
				damage = 5f;
			}
			else if (temp >= 700f)
			{
				interval = 35f;
				damage = 5f;
			}
			else if (temp >= 650f)
			{
				interval = 40f;
				damage = 5f;
			}

			if (timer < interval)
				return;

			timer = 0f;
			
			// =====================================================
			// SPEED MULTIPLIER
			// =====================================================

			float speedKmh = 0f;

			try
			{
				if (car.rb != null)
				{
					speedKmh =
						car.rb.velocity.magnitude * 3.6f;
				}
			}
			catch
			{
				speedKmh = 0f;
			}

			float speedMultiplier = 1f;

			if (speedKmh >= 90f)
			{
				speedMultiplier = 2.00f;
			}
			else if (speedKmh >= 80f)
			{
				speedMultiplier = 1.85f;
			}
			else if (speedKmh >= 70f)
			{
				speedMultiplier = 1.70f;
			}
			else if (speedKmh >= 60f)
			{
				speedMultiplier = 1.55f;
			}
			else if (speedKmh >= 50f)
			{
				speedMultiplier = 1.40f;
			}
			else if (speedKmh >= 40f)
			{
				speedMultiplier = 1.25f;
			}
			else if (speedKmh >= 30f)
			{
				speedMultiplier = 1.10f;
			}

			// APPLY MULTIPLIER
			damage *= speedMultiplier;

			// ------------------------------------------
			// APPLY DAMAGE
			// ------------------------------------------

			try
			{
				var method = car.CarDamage.GetType()
					.GetMethod(
						"DamageCar",
						BindingFlags.Public |
						BindingFlags.NonPublic |
						BindingFlags.Instance
					);

				if (method == null)
				{
					Debug.LogError(
						"[OverheatDamage] DamageCar method NOT FOUND"
					);
					return;
				}

				// ------------------------------------------------
				// APPLY REAL DAMAGE
				// ------------------------------------------------

				method.Invoke(
					car.CarDamage,
					new object[]
					{
						damage,
						true
					}
				); 

				ModLog.Overheat(
					$"[OverheatDamage] DAMAGE APPLIED " +
					$"| Car={car.ID} " +
					$"| Temp={temp:F0} " +
					$"| Speed={speedKmh:F0} km/h " +
					$"| SpeedMulti={speedMultiplier:F2} " +
					$"| Damage={(damage / 10000f):P2}"
				);
			}
			catch (Exception ex)
			{
				Debug.LogError(
					$"[OverheatDamage] Apply failed: {ex}"
				);
			}
		}
	}
}