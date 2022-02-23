using BepInEx;
using HarmonyLib;
using Receiver2;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using System.Reflection.Emit;
using FMODUnity;
using System.IO;

namespace Iskra2Patch
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private static readonly int gun_model = 1001;

        private static LinearMover bolt = new LinearMover();
        private static RotateMover bolt_lock = new RotateMover();
        private static LinearMover striker = new LinearMover();

        private static MethodInfo tryFireBullet;
        private static MethodInfo getLastBullet;
        private static FieldInfo bullet_shake_time;
        private static Type BulletInventory;

        //private static FMOD.Studio.EventInstance instance;

        private enum BoltState {
            Locked,
            Unlocked,
            Unlocking,
            Locking
        }

        private static BoltState bolt_state;

        private static bool pullingStriker;
        private static bool decocking;
        private static float round_chamber_amount = 0;

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            tryFireBullet = typeof(GunScript).GetMethod("TryFireBullet", BindingFlags.NonPublic | BindingFlags.Instance);
            getLastBullet = typeof(LocalAimHandler).GetMethod("GetLastMatchingLooseBullet", BindingFlags.NonPublic | BindingFlags.Instance);

            bullet_shake_time = typeof(LocalAimHandler).GetField("show_bullet_shake_time", BindingFlags.NonPublic | BindingFlags.Instance);

            BulletInventory = typeof(LocalAimHandler).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance).Single(t => t.Name == "BulletInventory");

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

                if (!codeMatcher.ReportFailure(__originalMethod, Debug.LogError)) {
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
                iskra = ___gun_prefabs_all.Single(go => {
                    GunScript gs = go.GetComponent<GunScript>();
                    return (int) gs.gun_model == gun_model;
                });
            } catch (Exception e) {
                Debug.Log(e.StackTrace);
                Debug.LogError("Couldn't load gun \"Iskra 2\"");
                return;
            }

            iskra.GetComponent<GunScript>().loaded_cartridge_prefab = __instance.generic_prefabs.Single(p => (p is ShellCasingScript) && ((ShellCasingScript) p).cartridge_type == CartridgeSpec.Preset._556_NATO).gameObject;

            __instance.generic_prefabs = new List<InventoryItem>(__instance.generic_prefabs) {
                iskra.GetComponent<GunScript>()
            }.ToArray();

            LocaleTactics lt = new LocaleTactics();

            lt.title = "Iskra";
            lt.gun_internal_name = "szikaka-test.iskra";
            lt.text = "A modded single shot rifle\nLong reload time makes it problematic to fire multiple shots, but its high accuracy and power ensures you don't need them";

            Locale.active_locale_tactics.Add("szikaka.iskra", lt);
        }

        [HarmonyPatch(typeof(GunScript), "Awake")]
        [HarmonyPrefix]
        private static void PatchGunAwake(ref GunScript __instance) {
            if ((int) __instance.gun_model != gun_model) return;

            bolt.transform = __instance.transform.Find("bolt");

            bolt.positions[0] = __instance.transform.Find("bolt/bolt_in").localPosition;
            bolt.positions[1] = __instance.transform.Find("bolt/bolt_out").localPosition;

            bolt_lock.transform = __instance.transform.Find("bolt/bolt_body");
            bolt_lock.rotations[0] = __instance.transform.Find("bolt/bolt_locked").rotation;
            bolt_lock.rotations[1] = __instance.transform.Find("bolt/bolt_unlocked").rotation;

            striker.transform = __instance.transform.Find("bolt/striker");
            striker.positions[0] = __instance.transform.Find("bolt/striker_in").localPosition;
            striker.positions[1] = __instance.transform.Find("bolt/striker_out").localPosition;

            bolt_state = BoltState.Locked;
        }

        [HarmonyPatch(typeof(GunScript), "Update")]
        [HarmonyPostfix]
        private static void PatchGunUpdate(ref GunScript __instance) {
            if ((int) __instance.gun_model != gun_model || Time.timeScale == 0) return;

            __instance.yoke_stage = (YokeStage) bolt_state;
            __instance.ApplyTransform("sear", __instance.trigger.amount, __instance.transform.Find("sear"));

            LocalAimHandler handler = LocalAimHandler.player_instance;

            pullingStriker = handler.character_input.GetButton(14);

            if (pullingStriker && bolt_state == BoltState.Locked) {
                striker.asleep = false;
                striker.target_amount = 1f;
                striker.accel = 85;
            }
            else if (bolt_state == BoltState.Locked && striker.amount != 1) {
                striker.target_amount = 0f;
                striker.accel = -100;
            }
            
            if (handler.character_input.GetButtonDown(11) && bolt_state != BoltState.Unlocked) {
                bolt_state = BoltState.Unlocking;
            }

            if (handler.character_input.GetButtonDown(10) && bolt_state != BoltState.Locked) {
                bolt_state = BoltState.Locking;
            }

            if (bolt_state == BoltState.Locked) {
                bolt_lock.amount = 0;
            }

            if (bolt_state == BoltState.Unlocking) {
                if (bolt.amount == 1) bolt_state = BoltState.Unlocked;

                if (bolt.amount == 0 && bolt_lock.amount != 1) {
                    bolt_lock.asleep = false;
                    bolt_lock.target_amount = 1;
                    bolt_lock.accel = 100;
                }
                else {
                    bolt.asleep = false;
                    bolt.target_amount = 1;
                    bolt.accel = 50;
                }

                if (bolt.transform.localPosition.z >= striker.positions[1].z && striker.amount != 0) {
                    striker.asleep = true;
                    striker.accel = 0;
                    striker.vel = 0;
                    striker.amount = (striker.positions[1].z - bolt.transform.localPosition.z) * (1 / striker.positions[1].z);
                }

                //if (bolt.amount <= 1f/9f && striker.amount != 0) {
                //    striker.amount = ((1f/9f) - bolt.amount) * 9;
                //}

                if (bolt.amount > 0.8 && __instance.round_in_chamber != null && __instance.round_in_chamber.transform.parent == bolt.transform) __instance.EjectRoundInChamber(0.4f);
            }

            if (bolt_state == BoltState.Locking) {
                if (bolt_lock.amount == 0) {
                    bolt_state = BoltState.Locked;
                    striker.asleep = true;
                    striker.amount = 1;
                    striker.accel = 0;
                }

                if (bolt.transform.localPosition.z >= striker.positions[1].z && bolt_lock.amount == 1 && __instance.trigger.amount != 1) {
                    striker.amount = (striker.positions[1].z - bolt.transform.localPosition.z) * (1 / striker.positions[1].z);
                }

                if (bolt.amount == 0 && bolt_lock.amount != 0) {
                    bolt_lock.asleep = false;
                    bolt_lock.target_amount = 0;
                    bolt_lock.accel = -100;
                }
                else {
                    bolt.asleep = false;
                    bolt.target_amount = 0;
                    bolt.accel = -50;
                }
            }

            if (bolt.amount == 0 && __instance.round_in_chamber != null) {
                __instance.round_in_chamber.transform.parent = bolt.transform;
            }

            if (handler.character_input.GetButtonDown(70) && bolt_state == BoltState.Unlocked ) {
                if (__instance.round_in_chamber == null) {
                    var bullet = getLastBullet.Invoke(handler, new object[]
				    {
                        new CartridgeSpec.Preset[] { __instance.loaded_cartridge_prefab.GetComponent<ShellCasingScript>().cartridge_type }
				    });

                    if (bullet != null) {
                        ShellCasingScript round = (ShellCasingScript) BulletInventory.GetField("item", BindingFlags.Public | BindingFlags.Instance).GetValue(bullet);

                        __instance.ReceiveRound(round);
                        handler.MoveInventoryItem(round, __instance.GetComponent<InventorySlot>());
                        round.transform.localScale = Vector3.one;
                        round.transform.position = __instance.transform.position;
                        round.transform.rotation = __instance.transform.rotation;
                        round_chamber_amount = 0;
                        AudioManager.PlayOneShotAttached("event:/guns/model10/insert_bullet", round.gameObject);
                    }
                }
                else {
                    bullet_shake_time.SetValue(handler, Time.time);
                }
            }

            if (round_chamber_amount < 1 && __instance.round_in_chamber != null) {
                __instance.ApplyTransform("round_chamber", round_chamber_amount, __instance.round_in_chamber.transform);
                round_chamber_amount += 0.08f * Time.timeScale;
            }

            if (striker.amount == 1) __instance.trigger.asleep = false;

            if (striker.amount == 0) decocking = false;

            if (__instance.trigger.amount == 1 && bolt_state == BoltState.Locked && striker.amount == 1) {
                if (decocking) {
                    striker.asleep = false;
                    striker.accel = -70;
                    striker.target_amount = 0;
                }
                else {
                    if (pullingStriker) {
                        decocking = true;
                    }
                    else {
                        tryFireBullet.Invoke(__instance, new object[] {1});
                        striker.asleep = false;
                        striker.amount = 0;
                        striker.target_amount = 0;
                    }
                }
            }

            bolt.TimeStep(Time.deltaTime);
            bolt_lock.TimeStep(Time.deltaTime);
            striker.TimeStep(Time.deltaTime);
            striker.UpdateDisplay();
        }
    }
}
