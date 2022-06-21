using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using System.Reflection.Emit;
using UnityEngine;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Receiver2;
using Wolfire;
using R2CustomSounds;

namespace Iskra2Patch {
	
	[BepInDependency("R2CustomSounds")]
	[BepInPlugin("pl.szikaka.iskra2", "Iskra 2 Patch", "1.0.0")]
	public class Plugin : BaseUnityPlugin {
		private static readonly int gun_model = 1001;

		private static Plugin plugin_instance;

		private static MethodInfo tryFireBullet;
		private static MethodInfo getLastBullet;
		private static FieldInfo bullet_shake_time;
		private static Type BulletInventory;

		public static ConfigEntry<string> sight_type;
		public static ConfigEntry<bool> use_custom_sounds;
		public static ConfigEntry<bool> indicator_active;
		public static ConfigEntry<string> indicator_background_color;
		public static ConfigEntry<string> indicator_color;

		public enum BoltState {
			Locked,
			Unlocked,
			Unlocking,
			Locking
		}

		private static Dictionary<string, string> customEvents = new Dictionary<string, string>() {
			{"event_fire", "custom:/iskra/gun/fire"},
			{"event_dry_fire", "custom:/iskra/gun/dry_fire"},
			{"event_unlock_bolt", "custom:/iskra/bolt/unlock"},
			{"event_lock_bolt", "custom:/iskra/bolt/lock"},
			{"event_open_bolt", "custom:/iskra/bolt/open"},
			{"event_close_bolt", "custom:/iskra/bolt/close"},
			{"event_load_round", "custom:/iskra/round/load"},
			{"event_cock_striker", "custom:/iskra/gun/striker"},
			{"event_change_magnification", "custom:/iskra/scope/click"}
		};
		// Most sounds come from this addon: https://steamcommunity.com/sharedfiles/filedetails/?id=2393318131&searchtext=arccw+fas+2
		// Firing sound is from Verdun https://store.steampowered.com/app/242860/Verdun/ , extracted by Heloft

		private static Dictionary<string, string> defaultEvents = new Dictionary<string, string>() {
			{"event_fire", "event:/guns/model10/shot"},
			{"event_dry_fire", "event:/guns/model10/dry_fire"},
			{"event_unlock_bolt", "event:/guns/deagle/slide_back_partial"},
			{"event_lock_bolt", "event:/guns/deagle/slide_back_partial"},
			{"event_open_bolt", "event:/guns/deagle/slide_back_partial"},
			{"event_close_bolt", "event:/guns/deagle/slide_back_partial"},
			{"event_load_round", "event:/guns/model10/insert_bullet"},
			{"event_cock_striker", "event:/guns/1911/cock"},
			{"event_change_magnification", "event:/newtonCradle_hit"}
		};

		public static Dictionary<string, string> soundEvents = defaultEvents;

		private void Awake() {
			Logger.LogInfo("Loaded Iskra 2 Plugin!");

			tryFireBullet = typeof(GunScript).GetMethod("TryFireBullet", BindingFlags.NonPublic | BindingFlags.Instance);
			getLastBullet = typeof(LocalAimHandler).GetMethod("GetLastMatchingLooseBullet", BindingFlags.NonPublic | BindingFlags.Instance);

			bullet_shake_time = typeof(LocalAimHandler).GetField("show_bullet_shake_time", BindingFlags.NonPublic | BindingFlags.Instance);

			BulletInventory = typeof(LocalAimHandler).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance).Single(t => t.Name == "BulletInventory");

			use_custom_sounds = Config.Bind("Gun settings", "Use custom sounds", true, "Should the gun use custom sounds? \nCustom sounds don't work if tapes are muted");

			indicator_active = Config.Bind("Scope settings", "Zoom indicator", true, "Show a zoom indicator on the top of scope's viewport");
			indicator_background_color = Config.Bind("Scope settings", "Zoom indicator background color", "#1a1a1a", "Color of zoom indicator background");
			indicator_color = Config.Bind("Scope settings", "Zoom indicator needle color", "#707070", "Color of zoom indicator needle");

			plugin_instance = this;

			ModAudioManager.LoadCustomEvents("iskra", Application.persistentDataPath + "/Guns/Iskra_2/Sounds");

			Harmony.CreateAndPatchAll(this.GetType());
			Harmony.CreateAndPatchAll(typeof(PopulateItemsTranspiler));
		}

		[HarmonyPatch(typeof(RuntimeTileLevelGenerator), "PopulateItems")]
		static class PopulateItemsTranspiler {
			private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
				CodeMatcher codeMatcher = new CodeMatcher(instructions).MatchForward(false, 
					new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(GunScript), "gun_type")),
					new CodeMatch(OpCodes.Ldc_I4_1)
				);

				if (!codeMatcher.ReportFailure(__originalMethod, Debug.Log)) {
					codeMatcher.SetOperandAndAdvance(
						AccessTools.Field(typeof(GunScript), "magazine_root_types")
					).InsertAndAdvance(
						new CodeInstruction(OpCodes.Ldlen)
					).SetOpcodeAndAdvance(
						OpCodes.Ldc_I4_0
					).SetOpcodeAndAdvance(
						OpCodes.Bne_Un_S
					);
				}

				return codeMatcher.InstructionEnumeration();
			}
		}

		[HarmonyPatch(typeof(ReceiverCoreScript), "Awake")]
		[HarmonyPostfix]
		private static void PatchCoreAwake(ref ReceiverCoreScript __instance, ref GameObject[] ___gun_prefabs_all) {
			GameObject iskra = null;

			try {
				iskra = ___gun_prefabs_all.First( go => (int) go.GetComponent<GunScript>().gun_model == gun_model );

				iskra.GetComponent<GunScript>().loaded_cartridge_prefab = __instance.generic_prefabs.First( p => p is ShellCasingScript && ((ShellCasingScript) p).cartridge_type == CartridgeSpec.Preset._556_NATO ).gameObject;

				iskra.GetComponent<GunScript>().pooled_muzzle_flash = ___gun_prefabs_all.First(go => go.GetComponent<GunScript>().gun_model == GunModel.Model10).GetComponent<GunScript>().pooled_muzzle_flash;

				var magazine = iskra.transform.Find("magazine").gameObject.AddComponent<SimpleMagazineScript>();
				magazine.round_prefab = iskra.GetComponent<GunScript>().loaded_cartridge_prefab;

				iskra.transform.Find("scope").gameObject.AddComponent<ScopeScript>();

				__instance.generic_prefabs = new List<InventoryItem>(__instance.generic_prefabs) {
					iskra.GetComponent<GunScript>()
				}.ToArray();

				LocaleTactics lt = new LocaleTactics();

				lt.title = "LTJ Iskra";
				lt.gun_internal_name = "szikaka.iskra";
				lt.text = 
					"A modded bolt-action rifle\n" +
					"An intermidiate caliber hunting rifle. Repurposed by LTJ Industries for post-mindkill use, it comes with a rail for mounting various attachments. While not originally designed for anti-robot use, a 5.56 caliber bullet can punch through hog's skull; a glorified computer case would be an afterthought."
				;

				Locale.active_locale_tactics.Add("szikaka.iskra", lt);

				__instance.PlayerData.unlocked_gun_names.Add("szikaka.iskra");

			} catch (Exception e) {
				Debug.LogError("Couldn't load gun \"Iskra 2\"");
				Debug.Log(e.StackTrace);
				return;
			}
		}

		[HarmonyPatch(typeof(GunScript), "Awake")]
		[HarmonyPrefix]
		private static void PatchGunAwake(ref GunScript __instance) {
			if ((int) __instance.gun_model != gun_model) return;

			
			var properties = __instance.gameObject.AddComponent<Iskra2WeaponProperties>();

			properties.bolt.transform = __instance.transform.Find("bolt");

			properties.bolt.positions[0] = __instance.transform.Find("bolt/bolt_in").localPosition;
			properties.bolt.positions[1] = __instance.transform.Find("bolt/bolt_out").localPosition;

			properties.bolt_lock.transform = __instance.transform.Find("bolt/bolt_body");
			properties.bolt_lock.rotations[0] = __instance.transform.Find("bolt/bolt_locked").rotation;
			properties.bolt_lock.rotations[1] = __instance.transform.Find("bolt/bolt_unlocked").rotation;

			properties.striker.transform = __instance.transform.Find("bolt/striker");
			properties.striker.positions[0] = __instance.transform.Find("bolt/striker_in").localPosition;
			properties.striker.positions[1] = __instance.transform.Find("bolt/striker_out").localPosition;
			properties.striker.amount = 0;

			properties.bolt_state = BoltState.Locked;

			properties.magazine = __instance.GetComponentInChildren<SimpleMagazineScript>();

			PlayerLoadout loadout = ReceiverCoreScript.Instance().CurrentLoadout;
			try {
				var equipment = loadout.equipment.Single(eq => eq.internal_name == "Iskra_2_Magazine");

				properties.magazine.SetPersistentData(equipment.persistent_data);
			} catch (Exception) {
				if (ReceiverCoreScript.Instance().game_mode.GetGameMode() == GameMode.RankingCampaign || ReceiverCoreScript.Instance().game_mode.GetGameMode() == GameMode.Classic)
					properties.magazine.queue_rounds = UnityEngine.Random.RandomRangeInt(0, 6);
			}

			properties.sights.Clear();

			properties.sights.AddRange(
				new SightAttachment[] { 
					new SightAttachment(
						"Aperture sight", 
						__instance.transform.Find("aperture_sight"),
						__instance.transform.Find("pose_aim_down_sights_irons"),
						__instance.transform.Find("point_bullet_fire_irons")
					),
					new SightAttachment(
						"Notch sight", 
						__instance.transform.Find("notch_sight"),
						__instance.transform.Find("pose_aim_down_sights_irons"),
						__instance.transform.Find("point_bullet_fire_irons")
					),
					new SightAttachment(
						"Scope", 
						__instance.transform.Find("scope"),
						__instance.transform.Find("pose_aim_down_sights_scope"),
						__instance.transform.Find("point_bullet_fire_scope")
					),
				}
			);

			sight_type = plugin_instance.Config.Bind(
				new ConfigDefinition("Gun settings", "Sight type"), 
				properties.sights[0].name, 
				new ConfigDescription("What type of sight do you want to use", new AcceptableValueList<string>(
					(from sight in properties.sights select sight.name).ToArray()
				))
			);

			foreach (var sight in properties.sights) {
				if (sight.name != sight_type.Value) sight.Disable();
            }
			properties.sights.Single(sight => { return (sight.name == sight_type.Value); }).Enable(__instance);
		}

		[HarmonyPatch(typeof(GunScript), "Update")]
		[HarmonyPostfix]
		private static void PatchGunUpdate(ref GunScript __instance, ref int ___hammer_state) {
			if ((int) __instance.gun_model != gun_model || Time.timeScale == 0 || !__instance.enabled || __instance.GetHoldingPlayer() == null || LocalAimHandler.player_instance.hands[1].state != LocalAimHandler.Hand.State.HoldingGun) return;

			Profiler.Begin("Iskra2.Update");

			var properties = __instance.GetComponent<Iskra2WeaponProperties>();

			if (sight_type.Value != properties.currentSight.name) {
				foreach (var sight in properties.sights) {
					if (sight.name != sight_type.Value) sight.Disable();
				}
				properties.sights.Single(sight => { return (sight.name == sight_type.Value); }).Enable(__instance);
            }

			soundEvents = use_custom_sounds.Value ? customEvents : defaultEvents;

			properties.bolt.TimeStep(Time.deltaTime);
			properties.bolt_lock.TimeStep(Time.deltaTime);

			__instance.yoke_stage = (YokeStage) properties.bolt_state;
			__instance.ApplyTransform("sear", __instance.trigger.amount, __instance.transform.Find("sear"));
			__instance.ApplyTransform("trigger_bar", __instance.trigger.amount, __instance.transform.Find("trigger_bar"));

			LocalAimHandler handler = LocalAimHandler.player_instance;

			properties.pullingStriker = handler.character_input.GetButton(14);

			if (properties.striker.amount == 1 && ___hammer_state != 2) {
				if(properties.bolt_state == BoltState.Locked && !properties.press_check) ModAudioManager.PlayOneShotAttached(soundEvents["event_cock_striker"], properties.striker.transform.gameObject, 0.2f);
				___hammer_state = 2;
			}

			if (handler.character_input.GetButtonDown(11) && properties.bolt_state != BoltState.Unlocked) {
				if (properties.bolt_state == BoltState.Locked) ModAudioManager.PlayOneShotAttached(soundEvents["event_unlock_bolt"], __instance.gameObject);
				properties.bolt_state = BoltState.Unlocking;
			}

			if (handler.character_input.GetButtonDown(10) && properties.bolt_state != BoltState.Locked) {
				if (properties.bolt_state == BoltState.Unlocked) ModAudioManager.PlayOneShotAttached(soundEvents["event_close_bolt"], __instance.gameObject);
				properties.bolt_state = BoltState.Locking;
			}

			if (handler.character_input.GetButton(10) && handler.character_input.GetButton(6) && properties.bolt_state == BoltState.Locked) {
				if (handler.character_input.GetButtonDown(10) || handler.character_input.GetButtonDown(6)) ModAudioManager.PlayOneShotAttached(soundEvents["event_unlock_bolt"], __instance.gameObject);
				if (properties.bolt.transform.localPosition.z >= properties.striker.positions[1].z && properties.striker.amount != 0) {
					properties.striker.asleep = true;
					properties.striker.accel = 0;
					properties.striker.vel = 0;
					properties.striker.amount = Mathf.InverseLerp(properties.striker.positions[1].z, 0, properties.bolt.transform.localPosition.z);
				}

				if (properties.bolt.amount == 0 && properties.bolt_lock.amount != 1) {
					properties.bolt_lock.asleep = false;
					properties.bolt_lock.target_amount = 1;
					properties.bolt_lock.accel = 100;
				}
				else {
					properties.bolt.asleep = false;
					properties.bolt.target_amount = __instance.press_check_amount;
					properties.bolt.accel = 50;
				}

				properties.press_check = true;
			}
			else if (properties.bolt_state == BoltState.Locked) {
				if (properties.bolt.transform.localPosition.z >= properties.striker.positions[1].z && properties.bolt_lock.amount == 1 && __instance.trigger.amount != 1) {
					properties.striker.amount = Mathf.InverseLerp(properties.striker.positions[1].z, 0, properties.bolt.transform.localPosition.z);
				}

				if (properties.bolt.amount > 0) {
					properties.bolt.asleep = false;
					properties.bolt.target_amount = 0;
					properties.bolt.accel = -50;
				}
				else {
					if (properties.bolt_lock.amount == 1) ModAudioManager.PlayOneShotAttached(soundEvents["event_lock_bolt"], __instance.gameObject);
					properties.bolt_lock.asleep = false;
					properties.bolt_lock.target_amount = 0;
					properties.bolt_lock.accel = -100;
				}

				if (properties.bolt.amount == 0 && properties.bolt_lock.amount == 0) {
					properties.press_check = false;
				}
			}

			if (properties.pullingStriker && properties.bolt_state == BoltState.Locked && !properties.press_check) {
				properties.striker.asleep = false;
				properties.striker.target_amount = 1f;
				properties.striker.accel = 85;
			}
			else if (properties.bolt_lock.amount == 0f && properties.striker.amount != 1 && !properties.press_check) {
				properties.striker.target_amount = 0f;
				properties.striker.accel = -100;
			}

			if (properties.bolt_state == BoltState.Unlocking) {
				if (properties.bolt.amount == 1) {
					properties.bolt_state = BoltState.Unlocked;

					if (__instance.round_in_chamber != null && __instance.round_in_chamber.transform.parent == properties.bolt.transform) {
						if (handler.character_input.GetButton(70)) {
							__instance.round_in_chamber.Move(null);
							if(properties.magazine.AddRound(__instance.round_in_chamber)) {
								ModAudioManager.PlayOneShotAttached(soundEvents["event_load_round"], __instance.round_in_chamber.gameObject, 0.4f);
								__instance.round_in_chamber = null;
							}
							else __instance.EjectRoundInChamber(0.4f);
						}
						else __instance.EjectRoundInChamber(0.4f);
					}
				}
				else {
					if (properties.bolt.amount == 0 && properties.bolt_lock.amount != 1) {
						properties.bolt_lock.asleep = false;
						properties.bolt_lock.target_amount = 1;
						properties.bolt_lock.accel = 100;
					}
					else {
						if (properties.bolt.amount == 0) ModAudioManager.PlayOneShotAttached(soundEvents["event_open_bolt"], __instance.gameObject);
						properties.bolt.asleep = false;
						properties.bolt.target_amount = 1;
						properties.bolt.accel = 50;
					}

					if (properties.bolt.transform.localPosition.z >= properties.striker.positions[1].z && properties.striker.amount != 0) {
						properties.striker.asleep = true;
						properties.striker.accel = 0;
						properties.striker.vel = 0;
						properties.striker.amount = (properties.striker.positions[1].z - properties.bolt.transform.localPosition.z) * (1 / properties.striker.positions[1].z);
					}

					if (properties.bolt.amount > 0.8 && __instance.round_in_chamber != null && __instance.round_in_chamber.transform.parent == properties.bolt.transform) {
						if (handler.character_input.GetButton(70)) {
							__instance.round_in_chamber.Move(null);
							if(properties.magazine.AddRound(__instance.round_in_chamber)) {
								ModAudioManager.PlayOneShotAttached(soundEvents["event_load_round"], __instance.round_in_chamber.gameObject, 0.4f);
								__instance.round_in_chamber = null;
							}
							else __instance.EjectRoundInChamber(0.4f);
						}
						else __instance.EjectRoundInChamber(0.4f);
					}
				}
			}

			if (properties.bolt_state == BoltState.Locking) {
				if (properties.bolt_lock.amount == 0) {
					properties.bolt_state = BoltState.Locked;
					properties.striker.asleep = true;
					properties.striker.accel = 0;
				}

				if (properties.bolt.transform.localPosition.z >= properties.striker.positions[1].z && properties.bolt_lock.amount == 1 && __instance.trigger.amount != 1) {
					properties.striker.amount = (properties.striker.positions[1].z - properties.bolt.transform.localPosition.z) * (1 / properties.striker.positions[1].z);
				}

				if (properties.bolt.amount == 0 && properties.bolt_lock.amount != 0) {
					if (properties.bolt_lock.amount == 1) ModAudioManager.PlayOneShotAttached(soundEvents["event_lock_bolt"], __instance.gameObject);
					properties.bolt_lock.asleep = false;
					properties.bolt_lock.target_amount = 0;
					properties.bolt_lock.accel = -100;
				}
				else {
					properties.bolt.asleep = false;
					properties.bolt.target_amount = 0;
					properties.bolt.accel = -50;
				}

				if (properties.bolt.transform.localPosition.z >= __instance.transform.Find("magazine/round_top_right").localPosition.y && !__instance.round_in_chamber) {
					ShellCasingScript round = properties.magazine.RemoveRound();
					properties.magazine.round_insert_amount = 1f - float.Epsilon;

					if (round != null) {
						__instance.ReceiveRound(round);
						handler.MoveInventoryItem(round, __instance.GetComponent<InventorySlot>());
					}
				}
			}

			if (properties.bolt.amount == 0 && __instance.round_in_chamber != null) {
				__instance.round_in_chamber.transform.parent = properties.bolt.transform;
				__instance.round_in_chamber.transform.localPosition = Vector3.zero;
				__instance.round_in_chamber.transform.localRotation = Quaternion.identity;
			}

			if (handler.character_input.GetButtonDown(70) && properties.bolt_state == BoltState.Unlocked ) {
				if (__instance.round_in_chamber == null && properties.magazine.can_insert_round) {
					var bullet = getLastBullet.Invoke(handler, new object[] {
						new CartridgeSpec.Preset[] { __instance.loaded_cartridge_prefab.GetComponent<ShellCasingScript>().cartridge_type }
					});

					if (bullet != null) {
						ShellCasingScript round = (ShellCasingScript) BulletInventory.GetField("item", BindingFlags.Public | BindingFlags.Instance).GetValue(bullet);

						if (properties.magazine.AddRound(round)) {
							ModAudioManager.PlayOneShotAttached(soundEvents["event_load_round"], round.gameObject);
							ModAudioManager.PlayOneShotAttached("event:/Magazines/1911_mag_bullet_insert_horizontal", round.gameObject);
						} else bullet_shake_time.SetValue(handler, Time.time);
					} else bullet_shake_time.SetValue(handler, Time.time);
				}
			}

			if (properties.striker.amount == 1) __instance.trigger.asleep = false;

			if (properties.striker.amount == 0) {
				properties.decocking = false;
				___hammer_state = 0;
			}

			if (__instance.trigger.amount == 1 && properties.bolt_state == BoltState.Locked && properties.striker.amount == 1 && !properties.press_check) {
				if (properties.decocking) {
					properties.striker.asleep = false;
					properties.striker.accel = -70;
					properties.striker.target_amount = 0;
				}
				else {
					if (properties.pullingStriker) {
						properties.decocking = true;
					}
					else {
						__instance.sound_event_gunshot = soundEvents["event_fire"];
						__instance.sound_dry_fire = soundEvents["event_dry_fire"];
						tryFireBullet.Invoke(__instance, new object[] {1});
						properties.striker.asleep = false;
						properties.striker.amount = 0;
						properties.striker.target_amount = 0;
						___hammer_state = 0;
						if (!__instance.dry_fired)
							__instance.transform.Find("pose_aim_down_sights").localPosition += new Vector3(0, 0, -0.04f);
					}
				}
			}

			__instance.transform.Find("pose_aim_down_sights").localPosition = Vector3.MoveTowards(__instance.transform.Find("pose_aim_down_sights").localPosition, properties.currentSight.ads_pose.localPosition, Time.deltaTime / 3);

			properties.striker.TimeStep(Time.deltaTime);
			properties.striker.UpdateDisplay();

			Profiler.End();
		}

		[HarmonyPatch(typeof(LocalAimHandler), "GetCurrentLoadout")]
		[HarmonyPostfix]
		private static void PatchLAHGetLoadout(ref PlayerLoadout __result) {
			if (__result.gun_internal_name == "szikaka.iskra") {
				PlayerLoadoutEquipment equipment = new PlayerLoadoutEquipment();

				equipment.chance_of_presence = 1;
				equipment.randomize_slot = false;
				equipment.randomize_loaded_ammo_count = false;

				LocalAimHandler.player_instance.TryGetGun(out GunScript gunScript);

				SimpleMagazineScript mag = gunScript.GetComponentInChildren<SimpleMagazineScript>();

				equipment.internal_name = "Iskra_2_Magazine";
				equipment.persistent_data = mag.GetPersistentData();
				equipment.equipment_type = (EquipmentType) 4;

				__result.equipment.Add(equipment);
			}
		}
	}
}
