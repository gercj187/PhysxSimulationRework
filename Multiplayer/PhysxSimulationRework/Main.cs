using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace PhysxSimulationRework
{
    public static class Main
    {
        public static UnityModManager.ModEntry? Mod;

		public static PhysxSimulationReworkSettings? LocalSettings;
		public static PhysxSimulationReworkSettings? Settings;

        private static Harmony? harmony;		
		private static GUIStyle? clientInfoStyle;

        public static bool Load(UnityModManager.ModEntry modEntry)
		{
			Mod = modEntry;

			// NEW:
			// Persönliche Einstellungen einmal aus settings.xml laden.
			LocalSettings = UnityModManager.ModSettings
				.Load<PhysxSimulationReworkSettings>(modEntry);

			// NEW:
			// In Singleplayer und als Host werden zunächst die lokalen
			// Einstellungen direkt als Runtime-Einstellungen verwendet.
			Settings = LocalSettings;

			modEntry.OnGUI = OnGUI;
			modEntry.OnSaveGUI = OnSaveGUI;

			PSR_Multiplayer.Initialize();

			harmony = new Harmony(modEntry.Info.Id);
			harmony.PatchAll(Assembly.GetExecutingAssembly());

			modEntry.Logger.Log(
				"Mod loaded + Harmony patched"
			);

			return true;
		}
		
		// =========================================================
		// RUNTIME SETTINGS
		// =========================================================

		internal static void UseTemporaryHostSettings(PhysxSimulationReworkSettings hostSettings)
		{
			if (hostSettings == null)
				return;

			// NEW:
			// Nur die Runtime-Referenz wird ersetzt.
			// LocalSettings bleibt vollständig unangetastet.
			Settings = hostSettings;

			Mod?.Logger.Log(
				"[MP] Temporary host settings activated."
			);
		}

		internal static void RestoreLocalSettings()
		{
			if (LocalSettings == null)
				return;

			bool turntableSoundChanged =
				Settings != null &&
				Settings.turntableWarningSound !=
				LocalSettings.turntableWarningSound;

			// NEW:
			// Nach Verlassen einer Client-Sitzung wieder die
			// persönlichen lokalen Einstellungen verwenden.
			Settings = LocalSettings;

			if (turntableSoundChanged)
			{
				TurntableTweaks.NotifyBellSoundChanged();
			}

			Mod?.Logger.Log(
				"[MP] Local settings restored."
			);
		}

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (PSR_Multiplayer.IsClient)
            {
                if (clientInfoStyle == null)
                {
                    clientInfoStyle = new GUIStyle(GUI.skin.box)
					{
						wordWrap = true,
						richText = true,
						fontSize = 14,

						padding = new RectOffset(
							12,
							12,
							12,
							12
						)
					};
                }

                GUILayout.Box(
                    "MULTIPLAYER CLIENT\n" +
                    "Physx Simulation Rework settings " +
                    "are controlled by the host.",
                    clientInfoStyle,
                    GUILayout.ExpandWidth(true)
                );

                return;
            }

            LocalSettings?.Draw(modEntry);
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
		{
			if (PSR_Multiplayer.IsClient)
				return;

			LocalSettings?.Save(modEntry);
		}
    }
	
	internal static class ModLog
	{
		private const string MOD = "[PhysxSimulationRework]";
		private static PhysxSimulationReworkSettings? S
			=> Main.Settings;
		// -------------------------------------------------
		// Core Logger
		// -------------------------------------------------
		private static void Log(bool enabled, string channel, string msg)
		{
			if (!enabled)
				return;

			Debug.Log($"{MOD}[DEBUG-{channel}] {msg}");
		}	
		// -------------------------
		// Turntable
		// -------------------------
		internal static void Turntable(string msg)
		{
			Log(S != null && S.enableTurntableLog, "Turntable", msg);
		}

		// -------------------------
		// Brakepipe
		// -------------------------
		internal static void Brakepipe(string msg)
		{
			Log(S != null && S.enableBrakepipeLog, "Brakepipe", msg);
		}

		// -------------------------
		// Derail
		// -------------------------
		internal static void Derail(string msg)
		{
			Log(S != null && S.enableDerailLog, "Derail", msg);
		}

		// -------------------------
		// Wheel
		// -------------------------
		internal static void Wheel(string msg)
		{
			Log(S != null && S.enableWheelLog, "Wheel", msg);
		}

		// -------------------------
		// Risk
		// -------------------------
		internal static void Risk(string msg)
		{
			Log(S != null && S.enableRiskLog, "Risk", msg);
		}

		// -------------------------
		// Coupler / PhysX
		// -------------------------
		internal static void Coupler(string msg)
		{
			Log(S != null && S.enableCouplerLog, "Couplers", msg);
		}
	}
}
