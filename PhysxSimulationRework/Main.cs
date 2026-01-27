using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace PhysxSimulationRework
{
    public static class Main
    {
        public static UnityModManager.ModEntry? Mod;
        public static PhysxSimulationReworkSettings? Settings;

        private static Harmony? harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            Settings = UnityModManager.ModSettings
                .Load<PhysxSimulationReworkSettings>(modEntry);

            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            modEntry.Logger.Log("Mod loaded + Harmony patched");
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            Settings?.Draw(modEntry);
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings?.Save(modEntry);
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
		// Coupler / PhysX
		// -------------------------
		internal static void Coupler(string msg)
		{
			Log(S != null && S.enableCouplerLog, "Couplers", msg);
		}
	}
}
