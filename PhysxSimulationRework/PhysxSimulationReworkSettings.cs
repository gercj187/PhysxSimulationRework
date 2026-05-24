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
	
	public enum DamageScalingMode
	{
		Half,     // /2
		Normal,   // x1
		High      // x1.5
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
        public float chanceToBreakOnDerail = 1.0f;
        public float chanceToBreakOnStress = 1.0f;
        public float customBreakForce = 750000f;
				
		// DYNAMIC DERAIL RISK
		public bool enableDynamicDerailRisk = true; 
		public float baseSafeSpeed = 10f;
		public float derailInterval = 10f;
		public DamageScalingMode damageScaling = DamageScalingMode.Normal;
		public float riskIncreasePerHit = 1f;
		public float riskDecreaseOnFail = 0.25f;
		public float riskThreshold = 3f;
		
		public bool enableBrakeOverheatDamage = true;
		
		//DEBUG
		public bool enableDerailDebug = false;
		//LOGS
		public bool enableTurntableLog = false;
		public bool enableBrakepipeLog = false;
		public bool enableDerailLog = false;
		public bool enableOverheatLog = false;
		public bool enableRiskLog = false;
		public bool enableCouplerLog = false;

        public override void Save(UnityModManager.ModEntry modEntry)
		{
			Save(this, modEntry);

			ModLog.Derail("Settings reloaded live");
		}
		
		// =========================
		// HELPER
		// =========================	
		
		private GUIStyle? _headerStyle;
		private GUIStyle? _cellStyle;

		private Texture2D CreateTexture(Color col)
		{
			Texture2D tex = new Texture2D(1, 1);
			tex.SetPixel(0, 0, col);
			tex.Apply();
			return tex;
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
		
		// =========================
        // CALCULATION (PREVIEW)
        // =========================
        private float CalculatePreviewChance(float damagePercent, float speedKmh)
        {
            float condition = 100f - damagePercent;
			float safeSpeed = Mathf.Max(baseSafeSpeed, condition);

            if (speedKmh <= safeSpeed)
                return 0f;

            float overSpeed = speedKmh - safeSpeed;

            float speedFactor = speedKmh * 0.1f;

            float tier = Mathf.Floor(overSpeed / 10f);
            float tierMultiplier = 0.1f + (tier * 0.1f);

            float safeFactor = overSpeed * tierMultiplier;

            float baseDamage = damagePercent / 100f;

            float damageScale = 1f;

            switch (damageScaling)
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

            float chance = (speedFactor + safeFactor) * damageFactor;

            return Mathf.Clamp(chance, 0f, 100f);
        }

        // =========================
        // DEBUG TABLE
        // =========================
        private void DrawDerailTable()
		{
			// STYLE CACHE
			if (_headerStyle == null)
			{
				_headerStyle = new GUIStyle(GUI.skin.button);
				_headerStyle.normal.background = CreateTexture(new Color(0.7f, 0.7f, 0.7f));
				_headerStyle.normal.textColor = Color.black;
			}

			if (_cellStyle == null)
			{
				_cellStyle = new GUIStyle(GUI.skin.button);
				_cellStyle.normal.background = CreateTexture(new Color(0.3f, 0.3f, 0.3f));
			}

			float[] speeds = { 30f, 60f, 90f };
			float[] damages = { 0f, 25f, 50f, 75f, 100f };

            BeginHorizontal();
            Button("Damage", _headerStyle, Width(122));

            foreach (float s in speeds)
                Button($"{s} km/h", _headerStyle, Width(122));

            EndHorizontal();

            foreach (float d in damages)
            {
                BeginHorizontal();

                Button($"{d}%", _headerStyle, Width(122));

                foreach (float s in speeds)
                {
                    float chance = CalculatePreviewChance(d, s);

                    Color textColor = GetChanceColor(chance / 100f);

                    _cellStyle.normal.textColor = textColor;
					
					Button($"{chance:0.0}%", _cellStyle, Width(122));
                }

                EndHorizontal();
            }
        }

        // =========================
        // COLOR HELPER
        // =========================
        private Color GetChanceColor(float chance)
        {
            if (chance <= 0.009f)
                return Color.green;

            if (chance <= 0.05f)
                return Color.yellow;

            if (chance <= 0.25f)
                return new Color(1f, 0.5f, 0f);

            return Color.red;
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
			enableCouplerFailure = Toggle(enableCouplerFailure,	"Enable Coupler Failures");
			
			if (enableCouplerFailure)
			{		
				var italicStyle = new GUIStyle(GUI.skin.label)
				{
					richText = true,
					wordWrap = true
				};
				
				if (enableDerailDebug)
				{
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
				}
				if (chanceToBreakOnStress > 0f)
				{
					GUILayout.Label("<i>(Adjusts the chance of coupler failure when excessive tensile force is applied.)</i>", italicStyle);
					Space(5);
					// --- Bruchkraft (GUI in kN, intern in N) ---
					float breakForce_kN = customBreakForce / 1000f;

					Label($"Tensile Force Limit: {breakForce_kN:0} kN");
					// CHANGE: Slider mit 10 kN Raster
					float rawBreakForce = HorizontalSlider(
						breakForce_kN,
						10f,
						1500f,
						Width(500)
					);

					// CHANGE: Auf 10er Schritte runden
					breakForce_kN = Mathf.Round(rawBreakForce / 10f) * 10f;

					// CHANGE: Sicherheits-Clamp
					breakForce_kN = Mathf.Clamp(breakForce_kN, 10f, 1500f);
					GUILayout.Label("<i>(Defines the tensile force limit at which a coupler may fail under excessive load.)</i>", italicStyle);
					GUILayout.Label("<i>(Increasing this value makes couplers more resistant to failure.)</i>", italicStyle);
					Space(5);
					// Rückrechnung: kN → N
					customBreakForce = breakForce_kN * 1000f;

					if (Button("Restore Default Break Force (750 kN)", Width(500)))
					{
						customBreakForce = 750000f;
					}
				}
			}						
            Space(5);
			GUILayout.EndVertical();	
            Space(2);
			// ----------------------------------------
			// DYNAMIC DERAIL RISK
			// ----------------------------------------
			GUILayout.BeginVertical(GUI.skin.box);

			Label("<b>Damage Derail Settings:</b>");
			Space(5);

			// =========================
			// ENABLE TOGGLE
			// =========================
			enableDynamicDerailRisk = Toggle(enableDynamicDerailRisk,"Enable Damage Derail System");

			if (enableDynamicDerailRisk)
			{
				Space(5);

				// =========================
				// BASE SAFE SPEED
				// =========================
				Label($"Safe Speed: {baseSafeSpeed:0} km/h");
				baseSafeSpeed = HorizontalSlider(baseSafeSpeed, 0f, 25f, Width(500));

				Label("<i>(Safe speed threshold below which no derailment is triggered)</i>", new GUIStyle(GUI.skin.label)
				{
					richText = true
				});

				Space(5);

				// =========================
				// INTERVAL
				// =========================
				Label($"Reaction interval: {derailInterval:0} s");
				derailInterval = HorizontalSlider(derailInterval, 5f, 15f, Width(500));

				Label("<i>(time between derailment checks)</i>", new GUIStyle(GUI.skin.label)
				{
					richText = true
				});

				Space(10);

				// =========================
				// RISK SYSTEM
				// =========================
				Label("<b>Risk Settings:</b>", new GUIStyle(GUI.skin.label)
				{
					richText = true
				});

				Space(5);
				
				if (enableDerailDebug)
				{
					Label($"Risk gain rate per check: +{riskIncreasePerHit:0.0}");
					riskIncreasePerHit = HorizontalSlider(riskIncreasePerHit, 0.5f, 2.0f, Width(500));

					Label($"Risk decay rate per check: -{riskDecreaseOnFail:0.0}");
					riskDecreaseOnFail = HorizontalSlider(riskDecreaseOnFail, 0.1f, 1.0f, Width(500));

					Label($"Risk Threshold: {riskThreshold:0.0}");
					riskThreshold = HorizontalSlider(riskThreshold, 1.0f, 5.0f, Width(500));
					
					Label("<i>(Derailment triggers once the risk reaches this threshold.)</i>", new GUIStyle(GUI.skin.label)
					{
						richText = true
					});
				}			

				Label("<b>Risk Influence</b>");

                BeginHorizontal();

                if (Button("Low", Width(167)))
                    damageScaling = DamageScalingMode.Half;

                if (Button("Medium", Width(166)))
                    damageScaling = DamageScalingMode.Normal;

                if (Button("High", Width(167)))
                    damageScaling = DamageScalingMode.High;

                EndHorizontal();

				Label("<b>Chance Preview</b>", new GUIStyle(GUI.skin.label)
				{
					richText = true
				});

				DrawDerailTable();
			}
			Space(5);
			GUILayout.EndVertical();
			Space(2);
			// ----------------------------------------
			// OVERHEAT DAMAGE
			// ----------------------------------------
			GUILayout.BeginVertical(GUI.skin.box);

			Label("<b>Brake Overheating:</b>");
			Space(5);

			// =========================
			// ENABLE TOGGLE
			// =========================
			enableBrakeOverheatDamage = GUILayout.Toggle(enableBrakeOverheatDamage,"Enable brake overheat damage"); 			
			Space(5);
			GUILayout.EndVertical();
			Space(2);
        }
    }
}
