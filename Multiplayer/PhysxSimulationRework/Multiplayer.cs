// File: Multiplayer.cs
// PhysxSimulationRework multiplayer synchronization.

using System;
using System.Reflection;
using System.Collections.Generic;
using DV;
using DV.Common;
using DV.Utils;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using UnityEngine;

namespace PhysxSimulationRework
{
    // =========================================================
    // PACKETS
    // =========================================================

    public sealed class ServerBoundPhysxReadyPacket : IPacket
	{
		public bool Ready { get; set; }
	}
	
	public sealed class ServerBoundPhysxTurntableBellPacket : IPacket
	{
		public string EventToken { get; set; } = string.Empty;
		public float PositionX { get; set; }
		public float PositionY { get; set; }
		public float PositionZ { get; set; }
	}
	
	public sealed class ClientBoundPhysxTurntableBellPacket : IPacket
	{
		public string EventToken { get; set; } = string.Empty;
		public float PositionX { get; set; }
		public float PositionY { get; set; }
		public float PositionZ { get; set; }
	}	
	
	public sealed class ServerBoundPhysxTurntableStopPacket : IPacket
	{
		public string EventToken { get; set; } = string.Empty;
		public float PositionX { get; set; }
		public float PositionY { get; set; }
		public float PositionZ { get; set; }
	}

	public sealed class ClientBoundPhysxTurntableStopPacket : IPacket
	{
		public string EventToken { get; set; } = string.Empty;
		public float PositionX { get; set; }
		public float PositionY { get; set; }
		public float PositionZ { get; set; }
		
		public float StoppedAngle { get; set; }
	}
	
	public sealed class ClientBoundPhysxCouplerBreakPacket : IPacket
	{
		public int EventId { get; set; }
		public string CarGuidA { get; set; } = string.Empty;
		public bool IsFrontCouplerA { get; set; }
		public string CarGuidB { get; set; } = string.Empty;
		public bool IsFrontCouplerB { get; set; }
	}

    public sealed class ClientBoundPhysxSettingsPacket : IPacket
    {
        // TURNTABLE
        public bool EnableTurntableTweaks { get; set; }
        public bool EnablePushToDetect { get; set; }
        public float TurntableRotationSpeedMultiplier { get; set; }
        public float SnapAngleToleranceDeg { get; set; }
        public int TurntableWarningSound { get; set; }

        // BRAKE PIPE
        public bool EnableAsymmetricCockVenting { get; set; }

        // COUPLERS
        public bool EnableCouplerFailure { get; set; }
		public bool EnableStressCouplerBodyDamage { get; set; }
        public float CustomBreakForce { get; set; }

        // DYNAMIC DERAIL RISK
        public bool EnableDynamicDerailRisk { get; set; }
        public float BaseSafeSpeed { get; set; }
        public float DerailInterval { get; set; }
        public int DamageScaling { get; set; }
        public float RiskIncreasePerHit { get; set; }
        public float RiskDecreaseOnFail { get; set; }
        public float RiskThreshold { get; set; }

        // WHEEL DAMAGE
        public bool EnableWheelDamage { get; set; }
        public bool EnableBrakeOverheatDamage { get; set; }
        public float OverheatBaseDamage { get; set; }
        public bool EnableWheelslideDamage { get; set; }
        public float WheelslideBaseDamage { get; set; }
    }

    public enum PhysxWorldEventType
    {
        Derail = 1,
        SetDamagePercentage = 2
    }

    public sealed class ClientBoundPhysxWorldEventPacket : IPacket
    {
        public int EventId { get; set; }

        public int EventType { get; set; }

        public string CarGuid { get; set; } = string.Empty;

        public float TargetDamagePercentage { get; set; }

        public string Reason { get; set; } = string.Empty;
    }

    // =========================================================
    // MULTIPLAYER CORE
    // =========================================================

    internal static class PSR_Multiplayer
    {
        private static GameObject? runtimeObject;

        private static int nextWorldEventId = 1;

        public static bool HasReceivedHostSettings { get; private set; }

        public static bool IsHost
        {
            get
            {
                try
                {
                    return MultiplayerAPI.Instance != null &&
                           MultiplayerAPI.Server != null &&
                           MultiplayerAPI.Instance.IsHost;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool IsClient
        {
            get
            {
                try
                {
                    return MultiplayerAPI.Instance != null &&
                           MultiplayerAPI.Client != null &&
                           !MultiplayerAPI.Instance.IsHost;
                }
                catch
                {
                    return false;
                }
            }
        }

        // NEW:
        // Diese Standalone-Version erzeugt Gameplay-Ereignisse
        // ausschließlich auf dem Host.
        public static bool CanRunAuthoritativeGameplay => IsHost;

        public static void Initialize()
        {
            if (runtimeObject != null)
                return;

            runtimeObject = new GameObject(
                "PhysxSimulationRework_Multiplayer"
            );

            UnityEngine.Object.DontDestroyOnLoad(runtimeObject);

            runtimeObject.AddComponent<
                PhysxSimulationReworkMPClient>();

            runtimeObject.AddComponent<
                PhysxSimulationReworkMPServer>();

            Main.Mod?.Logger.Log(
                "[MP] Multiplayer runtime created."
            );
        }

        public static void NotifySettingsChanged()
        {
            if (!IsHost)
                return;

            PhysxSimulationReworkMPServer.Instance
                ?.BroadcastSettings();
        }

        internal static void MarkSettingsReceived()
        {
            HasReceivedHostSettings = true;
        }

        internal static int GetNextWorldEventId()
        {
            int id = nextWorldEventId;
            nextWorldEventId++;

            if (nextWorldEventId <= 0)
                nextWorldEventId = 1;

            return id;
        }

		// =====================================================
		// TURNTABLE WARNING SOUND
		// =====================================================

		public static void NotifyLocalTurntableBell(TurntableController turntableController)
		{
			if (turntableController == null)
				return;

			Vector3 position =
				TurntableTweaks.GetTurntableWorldPosition(
					turntableController
				);

			string eventToken =
				Guid.NewGuid().ToString("N");

			TurntableTweaks.PlayBellLocal(
				turntableController
			);

			if (IsHost)
			{
				var packet =
					new ClientBoundPhysxTurntableBellPacket
					{
						EventToken = eventToken,
						PositionX = position.x,
						PositionY = position.y,
						PositionZ = position.z
					};

				PhysxSimulationReworkMPServer.Instance
					?.BroadcastTurntableBell(packet);

				return;
			}

			if (IsClient)
			{
				var packet =
					new ServerBoundPhysxTurntableBellPacket
					{
						EventToken = eventToken,
						PositionX = position.x,
						PositionY = position.y,
						PositionZ = position.z
					};

				PhysxSimulationReworkMPClient.Instance
					?.SendTurntableBell(packet);
			}
		}		

		// =====================================================
		// TURNTABLE SNAPPING EMERGENCY STOP
		// =====================================================
		
		public static void NotifyLocalTurntablePushStop(
			TurntableController turntableController)
		{
			if (turntableController == null)
				return;

			if (!TurntableTweaks.IsSnappingToTarget(
				turntableController))
			{
				return;
			}

			Vector3 position =
				TurntableTweaks.GetTurntableWorldPosition(
					turntableController
				);

			string eventToken =
				Guid.NewGuid().ToString("N");

			if (!IsHost && !IsClient)
			{
				TurntableTweaks.ApplyEmergencyStop(
					turntableController,
					"Singleplayer push"
				);

				return;
			}

			if (IsHost)
			{
				float stoppedAngle;

				bool stopped =
					TurntableTweaks.ApplyEmergencyStop(
						turntableController,
						"Host push",
						out stoppedAngle
					);

				if (!stopped)
					return;

				var packet =
					new ClientBoundPhysxTurntableStopPacket
					{
						EventToken = eventToken,
						PositionX = position.x,
						PositionY = position.y,
						PositionZ = position.z,
						StoppedAngle = stoppedAngle
					};

				PhysxSimulationReworkMPServer.Instance
					?.BroadcastTurntableStop(packet);

				return;
			}

			if (IsClient)
			{
				var packet =
					new ServerBoundPhysxTurntableStopPacket
					{
						EventToken = eventToken,
						PositionX = position.x,
						PositionY = position.y,
						PositionZ = position.z
					};

				PhysxSimulationReworkMPClient.Instance
					?.SendTurntableStop(packet);
			}
		}
		
		// =====================================================
		// COUPLER BREAK SYNCHRONIZATION
		// =====================================================
		public static void HostBroadcastCouplerBreak(Coupler couplerA,Coupler couplerB)
		{
			if (!IsHost ||
				couplerA == null ||
				couplerB == null)
			{
				return;
			}

			TrainCar? carA =
				couplerA.GetComponentInParent<TrainCar>();

			TrainCar? carB =
				couplerB.GetComponentInParent<TrainCar>();

			if (carA == null || carB == null)
			{
				Main.Mod?.Logger.Warning(
					"[MP] Coupler break synchronization failed: " +
					"one or both TrainCars were not found."
				);

				return;
			}

			string carGuidA = carA.CarGUID ?? string.Empty;
			string carGuidB = carB.CarGUID ?? string.Empty;

			if (string.IsNullOrEmpty(carGuidA) ||
				string.IsNullOrEmpty(carGuidB))
			{
				Main.Mod?.Logger.Warning(
					$"[MP] Coupler break synchronization failed: " +
					$"missing CarGUID " +
					$"| CarA={carA.ID} " +
					$"| CarB={carB.ID}"
				);

				return;
			}

			bool isFrontA =
				ReferenceEquals(
					carA.frontCoupler,
					couplerA
				);

			bool isRearA =
				ReferenceEquals(
					carA.rearCoupler,
					couplerA
				);

			bool isFrontB =
				ReferenceEquals(
					carB.frontCoupler,
					couplerB
				);

			bool isRearB =
				ReferenceEquals(
					carB.rearCoupler,
					couplerB
				);

			if ((!isFrontA && !isRearA) ||
				(!isFrontB && !isRearB))
			{
				Main.Mod?.Logger.Warning(
					$"[MP] Coupler break synchronization failed: " +
					$"could not determine both coupler sides."
				);

				return;
			}

			var packet =
				new ClientBoundPhysxCouplerBreakPacket
				{
					EventId = GetNextWorldEventId(),
					CarGuidA = carGuidA,
					IsFrontCouplerA = isFrontA,
					CarGuidB = carGuidB,
					IsFrontCouplerB = isFrontB
				};

			PhysxSimulationReworkMPServer.Instance
				?.BroadcastCouplerBreak(packet);

			ModLog.Coupler(
				$"Host broadcast coupler break " +
				$"| A={carA.ID}/" +
				$"{(isFrontA ? "FRONT" : "REAR")} " +
				$"| B={carB.ID}/" +
				$"{(isFrontB ? "FRONT" : "REAR")} " +
				$"| EventId={packet.EventId}"
			);
		}

        // =====================================================
        // HOST GAMEPLAY EVENTS
        // =====================================================

		internal static void DerailCarLocally(TrainCar car,string reason)
		{
			if (car == null || car.derailed)
				return;

			Bogie? bogie = car.FrontBogie;

			if (bogie == null)
				bogie = car.RearBogie;

			if (bogie == null)
			{
				Main.Mod?.Logger.Error(
					$"[MP] Cannot derail car {car.ID}: " +
					"no bogie was found."
				);

				return;
			}

			bogie.Derail(
				string.IsNullOrEmpty(reason)
					? "Host synchronized derailment"
					: reason
			);
		}

		public static void HostDerailCar(TrainCar car,string reason)
        {
            if (!IsHost || car == null)
                return;

            if (!car.derailed)
			{
				DerailCarLocally(
					car,
					reason
				);
			}

            var packet = new ClientBoundPhysxWorldEventPacket
            {
                EventId = GetNextWorldEventId(),
                EventType = (int)PhysxWorldEventType.Derail,
                CarGuid = car.CarGUID ?? string.Empty,
                TargetDamagePercentage =
                    car.CarDamage != null
                        ? car.CarDamage.DamagePercentage
                        : 0f,
                Reason = reason ?? string.Empty
            };

            PhysxSimulationReworkMPServer.Instance
                ?.BroadcastWorldEvent(packet);
        }

        public static void HostBroadcastDamageState(TrainCar car,string reason)
        {
            if (!IsHost ||
                car == null ||
                car.CarDamage == null)
            {
                return;
            }

            var packet = new ClientBoundPhysxWorldEventPacket
            {
                EventId = GetNextWorldEventId(),
                EventType =
                    (int)PhysxWorldEventType.SetDamagePercentage,
                CarGuid = car.CarGUID ?? string.Empty,
                TargetDamagePercentage =
                    Mathf.Clamp01(
                        car.CarDamage.DamagePercentage
                    ),
                Reason = reason ?? string.Empty
            };

            PhysxSimulationReworkMPServer.Instance
                ?.BroadcastWorldEvent(packet);
        }
    }

    // =========================================================
    // CLIENT
    // =========================================================

    internal sealed class PhysxSimulationReworkMPClient : MonoBehaviour
    {
        public static PhysxSimulationReworkMPClient? Instance
        {
            get;
            private set;
        }

        private IClient? client;

        private bool registered;

        private bool snapshotReceived;

        private float nextRequestTime;

        private int lastWorldEventId;
		
		private int lastCouplerBreakEventId;

		private readonly HashSet<string>
			processedTurntableBellTokens =
				new HashSet<string>();

		private readonly Queue<string>
			processedTurntableBellTokenOrder =
				new Queue<string>();

		private const int MAX_TURNTABLE_TOKEN_COUNT = 64;

		private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void Update()
		{
			IClient? currentClient = MultiplayerAPI.Client;

			if (!ReferenceEquals(client, currentClient))
			{
				Main.RestoreLocalSettings();

				client = currentClient;
				registered = false;
				snapshotReceived = false;
				nextRequestTime = 0f;
				lastWorldEventId = 0;
				lastCouplerBreakEventId = 0;

				processedTurntableBellTokens.Clear();
				processedTurntableBellTokenOrder.Clear();
			}

			if (!registered)
			{
				TryRegister();
			}

			TryRequestSettings();
		}

        private void TryRegister()
        {
            client = MultiplayerAPI.Client;

            if (client == null)
                return;

            client.RegisterPacket<
                ClientBoundPhysxSettingsPacket>(
                OnSettingsReceived
            );

            client.RegisterPacket<
                ClientBoundPhysxWorldEventPacket>(
                OnWorldEventReceived
            );
			
			client.RegisterPacket<
				ClientBoundPhysxTurntableBellPacket>(
				OnTurntableBellReceived
			);
			
			client.RegisterPacket<
				ClientBoundPhysxTurntableStopPacket>(
				OnTurntableStopReceived
			);
			
			client.RegisterPacket<
				ClientBoundPhysxCouplerBreakPacket>(
				OnCouplerBreakReceived
			);

            registered = true;

            Main.Mod?.Logger.Log(
                "[MP] Client packets registered."
            );
        }

        private void TryRequestSettings()
        {
            if (!registered ||
                client == null ||
                !PSR_Multiplayer.IsClient ||
                snapshotReceived)
            {
                return;
            }

            if (Time.unscaledTime < nextRequestTime)
                return;

            client.SendPacketToServer(
                new ServerBoundPhysxReadyPacket
                {
                    Ready = true
                },
                reliable: true
            );

            // Retry until the settings packet arrives.
            nextRequestTime = Time.unscaledTime + 2f;
        }

        private static void ApplySettings(ClientBoundPhysxSettingsPacket packet)
		{
			if (packet == null)
				return;

			TurntableWarningSound newTurntableSound =
				(TurntableWarningSound)Mathf.Clamp(
					packet.TurntableWarningSound,
					0,
					1
				);

			bool turntableSoundChanged =
				Main.Settings != null &&
				Main.Settings.turntableWarningSound !=
				newTurntableSound;

			var runtimeSettings = new PhysxSimulationReworkSettings
			{
				enableTurntableTweaks = packet.EnableTurntableTweaks,

				enablePushToDetect = false,

				turntableRotationSpeedMultiplier =
					Mathf.Clamp(
						packet.TurntableRotationSpeedMultiplier,
						0.01f,
						10f
					),

				snapAngleToleranceDeg =
					Mathf.Clamp(
						packet.SnapAngleToleranceDeg,
						0f,
						180f
					),

				turntableWarningSound = newTurntableSound,
				enableAsymmetricCockVenting = packet.EnableAsymmetricCockVenting,
				enableCouplerFailure = packet.EnableCouplerFailure,
				enableStressCouplerBodyDamage = packet.EnableStressCouplerBodyDamage,

				customBreakForce =
					Mathf.Max(
						1f,
						packet.CustomBreakForce
					),

				enableDynamicDerailRisk = packet.EnableDynamicDerailRisk,

				baseSafeSpeed =
					Mathf.Max(
						0f,
						packet.BaseSafeSpeed
					),

				derailInterval =
					Mathf.Max(
						0.1f,
						packet.DerailInterval
					),

				damageScaling =
					(DamageScalingMode)Mathf.Clamp(
						packet.DamageScaling,
						0,
						2
					),

				riskIncreasePerHit =
					Mathf.Max(
						0f,
						packet.RiskIncreasePerHit
					),

				riskDecreaseOnFail =
					Mathf.Max(
						0f,
						packet.RiskDecreaseOnFail
					),

				riskThreshold =
					Mathf.Max(
						0.01f,
						packet.RiskThreshold
					),

					
				enableWheelDamage =	packet.EnableWheelDamage,
				enableBrakeOverheatDamage = packet.EnableBrakeOverheatDamage,

				overheatBaseDamage =
					Mathf.Max(
						0f,
						packet.OverheatBaseDamage
					),

				enableWheelslideDamage = packet.EnableWheelslideDamage,

				wheelslideBaseDamage =
					Mathf.Max(
						0f,
						packet.WheelslideBaseDamage
					)
			};
				
			if (Main.LocalSettings != null)
			{
				runtimeSettings.enableDerailDebug = Main.LocalSettings.enableDerailDebug;
				runtimeSettings.enableTurntableLog = Main.LocalSettings.enableTurntableLog;
				runtimeSettings.enableBrakepipeLog = Main.LocalSettings.enableBrakepipeLog;
				runtimeSettings.enableDerailLog = Main.LocalSettings.enableDerailLog;
				runtimeSettings.enableWheelLog = Main.LocalSettings.enableWheelLog;
				runtimeSettings.enableRiskLog = Main.LocalSettings.enableRiskLog;
				runtimeSettings.enableCouplerLog = Main.LocalSettings.enableCouplerLog;
			}
			
			Main.UseTemporaryHostSettings(
				runtimeSettings
			);

			if (turntableSoundChanged)
			{
				TurntableTweaks.NotifyBellSoundChanged();
			}
		}

        private void OnSettingsReceived(ClientBoundPhysxSettingsPacket packet)
        {
            if (packet == null)
                return;

            ApplySettings(packet);

            snapshotReceived = true;

            PSR_Multiplayer.MarkSettingsReceived();

            Main.Mod?.Logger.Log(
                "[MP] Host settings received and applied."
            );
        }

        public void SendTurntableBell(ServerBoundPhysxTurntableBellPacket packet)
		{
			if (packet == null ||
				client == null ||
				!registered ||
				!PSR_Multiplayer.IsClient)
			{
				return;
			}

			RememberTurntableBellToken(
				packet.EventToken
			);

			client.SendPacketToServer(
				packet,
				reliable: true
			);

			ModLog.Turntable(
				$"Sent turntable bell event to host " +
				$"| Token={packet.EventToken}"
			);
		}
		
		public void SendTurntableStop(ServerBoundPhysxTurntableStopPacket packet)
		{
			if (packet == null ||
				client == null ||
				!registered ||
				!PSR_Multiplayer.IsClient)
			{
				return;
			}

			client.SendPacketToServer(
				packet,
				reliable: true
			);

			ModLog.Turntable(
				$"Sent turntable snapping-stop request " +
				$"| Token={packet.EventToken}"
			);
		}

		private void OnTurntableBellReceived(ClientBoundPhysxTurntableBellPacket packet)
		{
			if (packet == null)
				return;

			if (string.IsNullOrEmpty(packet.EventToken))
				return;

			if (processedTurntableBellTokens.Contains(
				packet.EventToken))
			{
				ModLog.Turntable(
					$"Ignored own turntable bell echo " +
					$"| Token={packet.EventToken}"
				);

				return;
			}

			RememberTurntableBellToken(
				packet.EventToken
			);

			Vector3 position = new Vector3(
				packet.PositionX,
				packet.PositionY,
				packet.PositionZ
			);

			TurntableTweaks.PlayBellFromNetwork(
				position
			);

			ModLog.Turntable(
				$"Applied synchronized turntable bell " +
				$"| Position={position} " +
				$"| Token={packet.EventToken}"
			);
		}
		
		private void OnTurntableStopReceived(ClientBoundPhysxTurntableStopPacket packet)
		{
			if (packet == null)
				return;

			if (string.IsNullOrEmpty(packet.EventToken))
				return;

			Vector3 position =
				new Vector3(
					packet.PositionX,
					packet.PositionY,
					packet.PositionZ
				);

			bool applied =
				TurntableTweaks.ApplyEmergencyStopFromNetwork(
					position,
					packet.StoppedAngle
				);

			if (!applied)
			{
				ModLog.Turntable(
					$"Failed to apply host turntable stop " +
					$"| Position={position} " +
					$"| Angle={packet.StoppedAngle:F2}"
				);

				return;
			}

			ModLog.Turntable(
				$"Applied host turntable snapping stop " +
				$"| Position={position} " +
				$"| Angle={packet.StoppedAngle:F2} " +
				$"| Token={packet.EventToken}"
			);
		}

		private void RememberTurntableBellToken(string eventToken)
		{
			if (string.IsNullOrEmpty(eventToken))
				return;

			if (!processedTurntableBellTokens.Add(
				eventToken))
			{
				return;
			}

			processedTurntableBellTokenOrder.Enqueue(
				eventToken
			);

			while (
				processedTurntableBellTokenOrder.Count >
				MAX_TURNTABLE_TOKEN_COUNT)
			{
				string oldestToken =
					processedTurntableBellTokenOrder.Dequeue();

				processedTurntableBellTokens.Remove(
					oldestToken
				);
			}
		}
		
		private void OnCouplerBreakReceived(ClientBoundPhysxCouplerBreakPacket packet)
		{
			if (packet == null)
				return;

			if (packet.EventId <= lastCouplerBreakEventId)
			{
				return;
			}

			lastCouplerBreakEventId = packet.EventId;

			bool applied =
				CouplerBreakSynchronizer
					.ApplyNetworkCouplerBreak(
						packet.CarGuidA,
						packet.IsFrontCouplerA,
						packet.CarGuidB,
						packet.IsFrontCouplerB
					);

			if (!applied)
			{
				Main.Mod?.Logger.Warning(
					$"[MP] Failed to apply coupler break " +
					$"| A={packet.CarGuidA}/" +
					$"{(packet.IsFrontCouplerA ? "FRONT" : "REAR")} " +
					$"| B={packet.CarGuidB}/" +
					$"{(packet.IsFrontCouplerB ? "FRONT" : "REAR")} " +
					$"| EventId={packet.EventId}"
				);

				return;
			}

			ModLog.Coupler(
				$"Client applied synchronized coupler break " +
				$"| A={packet.CarGuidA}/" +
				$"{(packet.IsFrontCouplerA ? "FRONT" : "REAR")} " +
				$"| B={packet.CarGuidB}/" +
				$"{(packet.IsFrontCouplerB ? "FRONT" : "REAR")} " +
				$"| EventId={packet.EventId}"
			);
		}

		private void OnWorldEventReceived(ClientBoundPhysxWorldEventPacket packet)
		{
            if (packet == null)
                return;

            if (packet.EventId <= lastWorldEventId)
                return;

            lastWorldEventId = packet.EventId;

            TrainCar? car = FindCar(packet.CarGuid);

            if (car == null)
            {
                Main.Mod?.Logger.Warning(
                    $"[MP] Car not found for world event: " +
                    $"{packet.CarGuid}"
                );

                return;
            }

            var eventType = (PhysxWorldEventType)packet.EventType;

            switch (eventType)
            {
                case PhysxWorldEventType.Derail:
                    ApplyDerailEvent(car, packet);
                    break;

                case PhysxWorldEventType.SetDamagePercentage:
                    ApplyDamageState(car, packet);
                    break;

                default:
                    Main.Mod?.Logger.Warning(
                        $"[MP] Unknown world event type: " +
                        $"{packet.EventType}"
                    );
                    break;
            }
        }

        private static TrainCar? FindCar(string carGuid)
        {
            if (string.IsNullOrEmpty(carGuid))
                return null;

            TrainCarRegistry? registry =
                SingletonBehaviour<TrainCarRegistry>.Instance;

            if (registry == null)
                return null;

            return registry.GetTrainCarByCarGuid(carGuid);
        }

        private static void ApplyDerailEvent(TrainCar car,ClientBoundPhysxWorldEventPacket packet)
        {
            if (car == null)
                return;

            ApplyDamageState(car, packet);

            if (!car.derailed)
			{
				PSR_Multiplayer.DerailCarLocally(
					car,
					string.IsNullOrEmpty(packet.Reason)
						? "Host synchronized derailment"
						: packet.Reason
				);
			}

            Main.Mod?.Logger.Log(
                $"[MP] Host derail applied to {car.ID}."
            );
        }

        private static void ApplyDamageState(TrainCar car,ClientBoundPhysxWorldEventPacket packet)
        {
            if (car == null || car.CarDamage == null)
                return;

            float target =
                Mathf.Clamp01(
                    packet.TargetDamagePercentage
                );

            float current =
                Mathf.Clamp01(
                    car.CarDamage.DamagePercentage
                );

            float missingPercentage = target - current;

            if (missingPercentage <= 0.00001f)
                return;

            float rawDamage =
                missingPercentage * 10000f;

            MethodInfo? damageMethod =
                car.CarDamage.GetType().GetMethod(
                    "DamageCar",
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance
                );

            if (damageMethod == null)
            {
                Main.Mod?.Logger.Error(
                    "[MP] DamageCar method not found."
                );

                return;
            }

            damageMethod.Invoke(
                car.CarDamage,
                new object[]
                {
                    rawDamage,
                    true
                }
            );

            Main.Mod?.Logger.Log(
                $"[MP] Host damage state applied " +
                $"| Car={car.ID} " +
                $"| Target={target:P2}"
            );
        }

        private void OnDestroy()
		{
			Main.RestoreLocalSettings();

			if (Instance == this)
				Instance = null;
		}
    }

    // =========================================================
    // SERVER / HOST
    // =========================================================

    internal sealed class PhysxSimulationReworkMPServer : MonoBehaviour
    {
        public static PhysxSimulationReworkMPServer? Instance
        {
            get;
            private set;
        }

        private IServer? server;

        private bool registered;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void Update()
        {
            IServer? currentServer = MultiplayerAPI.Server;

            if (!ReferenceEquals(server, currentServer))
            {
                server = currentServer;
                registered = false;
            }

            if (!registered)
            {
                TryRegister();
            }
        }

        private void TryRegister()
        {
            server = MultiplayerAPI.Server;

            if (server == null)
                return;

            server.RegisterPacket<
                ServerBoundPhysxReadyPacket>(
                OnClientReady
            );
			
			server.RegisterPacket<
				ServerBoundPhysxTurntableBellPacket>(
				OnClientTurntableBell
			);
			
			server.RegisterPacket<
				ServerBoundPhysxTurntableStopPacket>(
				OnClientTurntableStop
			);

            registered = true;

            Main.Mod?.Logger.Log(
                "[MP] Server packet registered."
            );
        }

        private void OnClientReady(ServerBoundPhysxReadyPacket packet,IPlayer sender)
		{
			if (packet == null ||
				sender == null ||
				!packet.Ready)
			{
				return;
			}

			SendSettings(sender);
		}

		private void OnClientTurntableBell(ServerBoundPhysxTurntableBellPacket packet,IPlayer sender)
		{
			if (packet == null ||
				sender == null ||
				string.IsNullOrEmpty(packet.EventToken))
			{
				return;
			}

			Vector3 position = new Vector3(
				packet.PositionX,
				packet.PositionY,
				packet.PositionZ
			);

			TurntableTweaks.PlayBellFromNetwork(
				position
			);

			var clientPacket =
				new ClientBoundPhysxTurntableBellPacket
				{
					EventToken = packet.EventToken,
					PositionX = packet.PositionX,
					PositionY = packet.PositionY,
					PositionZ = packet.PositionZ
				};

			BroadcastTurntableBell(
				clientPacket
			);

			ModLog.Turntable(
				$"Received client turntable bell " +
				$"and broadcast it " +
				$"| Position={position} " +
				$"| Token={packet.EventToken}"
			);
		}
		
		private void OnClientTurntableStop(ServerBoundPhysxTurntableStopPacket packet,IPlayer sender)
		{
			if (packet == null ||
				sender == null ||
				string.IsNullOrEmpty(packet.EventToken))
			{
				return;
			}

			Vector3 position =new Vector3
			(
				packet.PositionX,
				packet.PositionY,
				packet.PositionZ
			);

			float stoppedAngle;
			
			bool stopped =
				TurntableTweaks.ApplyEmergencyStopFromNetwork(
					position,
					out stoppedAngle
				);

			if (!stopped)
			{
				ModLog.Turntable(
					$"Rejected turntable snapping-stop request " +
					$"| Reason=No active snapping movement " +
					$"| Position={position} " +
					$"| Token={packet.EventToken}"
				);

				return;
			}

			var clientPacket = new ClientBoundPhysxTurntableStopPacket
			{
				EventToken = packet.EventToken,
				PositionX = packet.PositionX,
				PositionY = packet.PositionY,
				PositionZ = packet.PositionZ,
				StoppedAngle = stoppedAngle
			};

			BroadcastTurntableStop(
				clientPacket
			);

			ModLog.Turntable(
				$"Client push stopped snapping on host " +
				$"| Position={position} " +
				$"| Angle={stoppedAngle:F2} " +
				$"| Token={packet.EventToken}"
			);
		}

		private static ClientBoundPhysxSettingsPacket CreateSettingsPacket()
        {
			PhysxSimulationReworkSettings settings =
				Main.LocalSettings ??
				Main.Settings ??
				new PhysxSimulationReworkSettings();

            return new ClientBoundPhysxSettingsPacket
            {
                EnableTurntableTweaks = settings.enableTurntableTweaks,
                EnablePushToDetect = false,
                TurntableRotationSpeedMultiplier = settings.turntableRotationSpeedMultiplier,
                SnapAngleToleranceDeg = settings.snapAngleToleranceDeg,
                TurntableWarningSound = (int)settings.turntableWarningSound,
                EnableAsymmetricCockVenting = settings.enableAsymmetricCockVenting,
                EnableCouplerFailure = settings.enableCouplerFailure,
				EnableStressCouplerBodyDamage = settings.enableStressCouplerBodyDamage,
                CustomBreakForce = settings.customBreakForce,
                EnableDynamicDerailRisk = settings.enableDynamicDerailRisk,
                BaseSafeSpeed = settings.baseSafeSpeed,
                DerailInterval = settings.derailInterval,
                DamageScaling = (int)settings.damageScaling,
                RiskIncreasePerHit = settings.riskIncreasePerHit,
                RiskDecreaseOnFail = settings.riskDecreaseOnFail,
                RiskThreshold = settings.riskThreshold,
                EnableWheelDamage = settings.enableWheelDamage,
                EnableBrakeOverheatDamage = settings.enableBrakeOverheatDamage,
                OverheatBaseDamage = settings.overheatBaseDamage,
                EnableWheelslideDamage = settings.enableWheelslideDamage,
                WheelslideBaseDamage = settings.wheelslideBaseDamage
            };
        }

        private void SendSettings(IPlayer player)
        {
            if (server == null || player == null)
                return;

            server.SendPacketToPlayer(
                CreateSettingsPacket(),
                player,
                reliable: true
            );

            Main.Mod?.Logger.Log(
                "[MP] Host settings sent to client."
            );
        }

        public void BroadcastSettings()
        {
            if (!registered ||
                server == null ||
                !PSR_Multiplayer.IsHost)
            {
                return;
            }

            server.SendPacketToAll(
                CreateSettingsPacket(),
                reliable: true,
                excludeSelf: true
            );

            Main.Mod?.Logger.Log(
                "[MP] Host settings broadcast."
            );
        }

        public void BroadcastTurntableBell(ClientBoundPhysxTurntableBellPacket packet)
		{
			if (!registered ||
				server == null ||
				!PSR_Multiplayer.IsHost ||
				packet == null)
			{
				return;
			}

			server.SendPacketToAll(
				packet,
				reliable: true,
				excludeSelf: true
			);

			ModLog.Turntable(
				$"Broadcast turntable bell " +
				$"| Token={packet.EventToken}"
			);
		}
		
		public void BroadcastTurntableStop(ClientBoundPhysxTurntableStopPacket packet)
		{
			if (!registered ||
				server == null ||
				!PSR_Multiplayer.IsHost ||
				packet == null)
			{
				return;
			}

			server.SendPacketToAll(
				packet,
				reliable: true,
				excludeSelf: true
			);

			ModLog.Turntable(
				$"Broadcast turntable snapping stop " +
				$"| Angle={packet.StoppedAngle:F2} " +
				$"| Token={packet.EventToken}"
			);
		}

		public void BroadcastCouplerBreak(ClientBoundPhysxCouplerBreakPacket packet)
		{
			if (!registered ||
				server == null ||
				!PSR_Multiplayer.IsHost ||
				packet == null)
			{
				return;
			}

			server.SendPacketToAll(
				packet,
				reliable: true,
				excludeSelf: true
			);

			ModLog.Coupler(
				$"Broadcast synchronized coupler break " +
				$"| A={packet.CarGuidA}/" +
				$"{(packet.IsFrontCouplerA ? "FRONT" : "REAR")} " +
				$"| B={packet.CarGuidB}/" +
				$"{(packet.IsFrontCouplerB ? "FRONT" : "REAR")} " +
				$"| EventId={packet.EventId}"
			);
		}

		public void BroadcastWorldEvent(ClientBoundPhysxWorldEventPacket packet)
		{
            if (!registered ||
                server == null ||
                !PSR_Multiplayer.IsHost ||
                packet == null)
            {
                return;
            }

            server.SendPacketToAll(
                packet,
                reliable: true,
                excludeSelf: true
            );
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}