using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;
using System;
using System.Collections.Generic;
using System.Reflection;
using DV.Utils;
using DV.Simulation;
using DV.Simulation.Brake;
using DV.CabControls;
using DV.Simulation.Cars;
using DV.VFX;

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
	
			bool isMoving = _rotationSoundIntensity(tc) > 0f;

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

					_rotationSoundIntensity(__instance) = Mathf.Max( _rotationSoundIntensity(__instance), 0.25f);
					
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

				cj.breakForce = 1f;
				cj.breakTorque = 1f;

				ModLog.Derail(
					$"[PhysxSimulationRework][PhysX] Coupler weakened by derailment " +
					$"| CarID={__instance.ID} | State={coupler.state}"
				);
			}
		}
	}

    // -----------------------------
	// Couplefails - FORCE BRAKE
	// -----------------------------
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
}