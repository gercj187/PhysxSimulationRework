using UnityModManagerNet;
using UnityEngine;
using static UnityEngine.GUILayout;

namespace PhysxSimulationRework
{		
	public enum TurntableWarningSound
	{
		DashWarningBell,
		TrainBell_DH4
	}
	
    public class PhysxSimulationReworkSettings : UnityModManager.ModSettings
    {
		//TURNTABLE
        public bool enableTurntableTweaks = true;
		public bool enablePushToDetect = false;
		
        public float turntableRotationSpeedMultiplier = 0.5f;
		public float snapAngleToleranceDeg = 20.0f;
		public TurntableWarningSound turntableWarningSound = TurntableWarningSound.DashWarningBell;
		
		//AIRBRAKECOCKS	
		public bool enableAsymmetricCockVenting = true;
				
		// COUPLER FAILURE		
		public bool enableCouplerFailure = true;
        public float chanceToBreakOnDerail = 0.5f;
        public float chanceToBreakOnStress = 0.35f;
        public float customBreakForce = 1000000f;
		
		//LOGS
		public bool enableTurntableLog = false;
		public bool enableBrakepipeLog = false;
		public bool enableDerailLog = false;
		public bool enableCouplerLog = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public void Draw(UnityModManager.ModEntry modEntry)
        {
			GUILayout.BeginVertical(GUI.skin.box);
            Space(5);
			Label("<b>Turntable Settings:</b>");
            enableTurntableTweaks = Toggle(enableTurntableTweaks, "Enable Turntable Modifications");
			if (enableTurntableTweaks)
			{
				Space(5);
				// ROTATION SPEED
				Label("Rotation Speed Multiplier:");
				BeginHorizontal();
				DrawRotationPresetButton(0.25f, "x0.25");
				DrawRotationPresetButton(0.50f, "x0.5");
				DrawRotationPresetButton(0.75f, "x0.75");
				DrawRotationPresetButton(1.00f, "x1.0");
				EndHorizontal();
				Space(5);				
				// SNAPPING TOLERANCE
				// SNAP TOLERANCE (10° steps + special labels)
				string snapTolLabel;
				if (Mathf.Abs(snapAngleToleranceDeg) < 0.01f)
				{
					snapTolLabel = "No Detection";
				}
				else if (Mathf.Abs(snapAngleToleranceDeg - 180f) < 0.01f)
				{
					snapTolLabel = "Detect next Track";
				}
				else
				{
					snapTolLabel = $"{snapAngleToleranceDeg:0}°";
				}
				Label($"Track detection: {snapTolLabel}");
				float rawTol = HorizontalSlider(snapAngleToleranceDeg,0.0f,180.0f,Width(500));
				// 👉 auf 10° Schritte runden
				float snappedTol = Mathf.Round(rawTol / 10f) * 10f;
				// sauber clampen
				snapAngleToleranceDeg = Mathf.Clamp(snappedTol, 0.0f, 180.0f);				
				Space(5);				
				// ----------------------------------------
				// Push-to-Detect (nur wenn Detection aktiv)
				// ----------------------------------------
				if (snapAngleToleranceDeg <= 0.0f)
				{
					enablePushToDetect = false;
				}
				else
				{
					enablePushToDetect = Toggle(enablePushToDetect,"Allow track detection while pushing (Push-to-Detect)");
				}
				Space(5);		
				Label("Turntable Warning Sound:");				
				BeginHorizontal();
				DrawSoundPresetButton(
					TurntableWarningSound.DashWarningBell,
					"Classic Mechanical Bell"
				);
				DrawSoundPresetButton(
					TurntableWarningSound.TrainBell_DH4,
					"Modern Electronic Horn"
				);
				EndHorizontal();
			}
            Space(5);
			GUILayout.EndVertical();	
            Space(2);
			// ----------------------------------------
			//	AIRBRAKECOCKS	
			// ----------------------------------------
			GUILayout.BeginVertical(GUI.skin.box);
			Label("<b>Brake Pipe Settings:</b>");
			Space(5);
			enableAsymmetricCockVenting = GUILayout.Toggle(enableAsymmetricCockVenting,"Enable Asymmetric Brake Pipe Venting");
			if (enableAsymmetricCockVenting)
			{				
				var italicStyle = new GUIStyle(GUI.skin.label)
				{
					richText = true,
					wordWrap = true
				};
				Space(5);
				GUILayout.Label("<i>(Simulates real brake pipe behavior by dumping the air when one\n"
						+ "angle cock is closed and the other is open, like in real life.)</i>",italicStyle);
			}	
            Space(5);
			GUILayout.EndVertical();	
            Space(2);
			// ----------------------------------------
			// 	COUPLER FAILURE
			// ----------------------------------------
			GUILayout.BeginVertical(GUI.skin.box);
			Label("<b>Coupler Settings:</b>");
			Space(5);
			enableCouplerFailure = Toggle(
				enableCouplerFailure,
				"Enable Coupler Failures"
			);
			if (enableCouplerFailure)
			{		
				var italicStyle = new GUIStyle(GUI.skin.label)
				{
					richText = true,
					wordWrap = true
				};
				// --- Entgleisung ---
				string derailLabel = chanceToBreakOnDerail <= 0f
					? "Chance on Derailment: (Disabled)"
					: $"Chance on Derailment: {Mathf.RoundToInt(chanceToBreakOnDerail * 100)}%";
				Label(derailLabel);
				chanceToBreakOnDerail = HorizontalSlider(chanceToBreakOnDerail, 0f, 1f, Width(500));
				if (chanceToBreakOnDerail > 0f)
				{
					GUILayout.Label("<i>(Adjusts the probability that couplers will fail when a vehicle derails.)</i>", italicStyle);
				}
				Space(5);
				// --- Überlast (Lose) ---
				string stressLabel = chanceToBreakOnStress <= 0f
					? "Stress Failure Chance: (Disabled)"
					: $"Stress Failure Chance: {Mathf.RoundToInt(chanceToBreakOnStress * 100)}%";

				Label(stressLabel);
				chanceToBreakOnStress = HorizontalSlider(chanceToBreakOnStress, 0f, 1f, Width(500));
				if (chanceToBreakOnStress > 0f)
				{
					GUILayout.Label("<i>(Adjusts the chance of coupler failure when excessive tensile force is applied.)</i>", italicStyle);
					Space(5);
					// --- Bruchkraft (GUI in kN, intern in N) ---
					float breakForce_kN = customBreakForce / 1000f;

					Label($"Tensile Force Limit: {breakForce_kN:0} kN");
					breakForce_kN = HorizontalSlider(
						breakForce_kN,
						1f,
						1500f,
						Width(500)
					);
					GUILayout.Label("<i>(Defines the tensile force limit at which a coupler may fail under excessive load.)</i>", italicStyle);
					GUILayout.Label("<i>(Increasing this value makes couplers more resistant to failure.)</i>", italicStyle);
					Space(5);
					// Rückrechnung: kN → N
					customBreakForce = breakForce_kN * 1000f;

					if (Button("Restore Default Break Force (1000 kN)", Width(500)))
					{
						customBreakForce = 1000000f;
					}
				}
			}     
            Space(5);
			GUILayout.EndVertical();			
        }
		private void DrawRotationPresetButton(float value, string label)
		{
			bool isActive = Mathf.Abs(turntableRotationSpeedMultiplier - value) < 0.001f;
			// Farbe merken
			Color prevColor = GUI.color;
			if (isActive)
			{
				GUI.color = Color.green;
			}
			if (GUILayout.Button(label, GUILayout.Width(122)))
			{
				turntableRotationSpeedMultiplier = value;
			}
			// Farbe zurücksetzen
			GUI.color = prevColor;
		}
		private void DrawSoundPresetButton(
			TurntableWarningSound value,
			string label
		)
		{
			bool isActive = turntableWarningSound == value;

			Color prev = GUI.color;
			if (isActive)
				GUI.color = Color.green;

			if (GUILayout.Button(label, GUILayout.Width(249)))
			{
				if (turntableWarningSound != value)
				{
					turntableWarningSound = value;
					TurntableTweaks.NotifyBellSoundChanged();
				}
			}

			GUI.color = prev;
		}
    }
}
