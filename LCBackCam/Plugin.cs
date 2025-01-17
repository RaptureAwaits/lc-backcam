using System;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;


using GameNetcodeStuff;

using BepInEx;
using BepInEx.Logging;

using HarmonyLib;

using LethalCompanyInputUtils.Api;
using LethalCompanyInputUtils.BindingPathEnums;

namespace LCBackCam {
    [BepInPlugin(mod_guid, mod_name, mod_version)]
    [BepInDependency("com.rune580.LethalCompanyInputUtils", BepInDependency.DependencyFlags.HardDependency)]
    public class LCBackCamBase : BaseUnityPlugin {
        private const string mod_guid = "raptureawaits.backcam";
		private const string mod_name = "Reverse Camera Keybind";
		private const string mod_version = "1.0.0";

        public static LCBackCamBase instance;
        public static ManualLogSource modlog;
        public static CreateKeybind keys;

        public static  float cam_distance = 5f;

        private readonly Harmony harmony = new(mod_guid);

        public int layer_mask;

        public Camera backcam;
        public Renderer visor_renderer;
        public bool cam_reversed = false;

        public class CreateKeybind : LcInputActions {
            [InputAction(KeyboardControl.R, Name = "Reverse Camera")]
            public InputAction ReverseCamKey { get; set; }
            [InputAction(KeyboardControl.Unbound, Name = "Output Camera Debug")]
            public InputAction CamInfoKey { get; set; }
        }

        public void CreateBackCam(Camera gamecam) {
            backcam = new GameObject().AddComponent<Camera>();
            backcam.CopyFrom(gamecam);

            backcam.name = "Backcam";
            // ___gameplayCamera.name =  "Gamecam"; // For the record: renaming the gameplay camera immediately breaks the player's arms
        }

        public void UpdateBackcamTransform(Camera gamecam) {
            RaycastHit hit_info;
            bool hit = Physics.Raycast(
                gamecam.transform.position,
                gamecam.transform.forward,
                out hit_info,
                cam_distance + 0.5f,
                layer_mask
            );

            float dist_to_obstacle = cam_distance;
            if (hit) {
                dist_to_obstacle = hit_info.distance - 0.5f;
            }

            UnityEngine.Vector3 new_pos = (
                gamecam.transform.position +
                dist_to_obstacle * gamecam.transform.forward
            );
            backcam.transform.SetPositionAndRotation(new_pos, gamecam.transform.rotation);
            backcam.transform.LookAt(gamecam.transform);
        }

        public void LogCameraInfo(Camera c) {
            modlog.LogInfo($"------------------------------ CAMERA --------------------------------");
            modlog.LogInfo($"Name: {c.name}");
            modlog.LogInfo($"- Enabled:   {c.enabled}\n");

            modlog.LogInfo($"- Pos:       {c.transform.position}");
            modlog.LogInfo($"- Rot:       {c.transform.rotation.eulerAngles}\n");

            modlog.LogInfo($"- Local Pos: {c.transform.position}");
            modlog.LogInfo($"- Local Rot: {c.transform.localRotation.eulerAngles}");
            modlog.LogInfo($"----------------------------------------------------------------------\n");
        }

        public void Awake() {
            keys = new CreateKeybind();

            if (instance == null) {
                instance = this;
            }
            modlog = BepInEx.Logging.Logger.CreateLogSource("BackCam");

            harmony.PatchAll();
            modlog.LogInfo($"Plugin {mod_guid} is loaded!");
        }
    }
}

namespace LCBackCam.Patches {
    [HarmonyPatch(typeof(PlayerControllerB))]
    internal class BackCamPatch {
        internal static ManualLogSource modlog = LCBackCamBase.modlog;
		internal static LCBackCamBase b = LCBackCamBase.instance;
        internal static LCBackCamBase.CreateKeybind keys = LCBackCamBase.keys;

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void UpdatePostfix(PlayerControllerB __instance, ref Camera ___gameplayCamera) {
            // Accidentally doing anything in this patch to a client other than the current player has really weird results: learned that the hard way
            bool is_this_player = __instance.IsOwner && __instance.isPlayerControlled;
            if (!is_this_player) {
                return;
            }

            // Create backwards facing camera
            if (b.backcam == null) {
                b.CreateBackCam(___gameplayCamera);
                modlog.LogInfo($"[{__instance.OwnerClientId}] Created reverse camera.");
            }

            // A collection of conditions which should disqualify the player from enabling the reverse camera
            bool can_reverse_cam = (
                !__instance.inTerminalMenu &&
                !__instance.isPlayerDead &&
                !__instance.isTypingChat &&
                b.backcam != null
            );

            // Check if bound key is pressed and that no disqualifying conditions apply, then switch the active camera to our reverse camera instance
            if (keys.ReverseCamKey.IsPressed() && can_reverse_cam && !__instance.isCameraDisabled) {
                b.UpdateBackcamTransform(___gameplayCamera);
                if (StartOfRound.Instance.activeCamera != b.backcam) {
                    StartOfRound.Instance.SwitchCamera(b.backcam);
                    ___gameplayCamera.enabled = false;
                    __instance.thisPlayerModelArms.enabled = false;
                    __instance.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
                    b.cam_reversed = true;
                }
            }

            // Check if bound key is released OR if any disqualifying conditions are applied whilst reverse camera is active, then revert the camera to normal gameplay instance
            if ((!keys.ReverseCamKey.IsPressed() || !can_reverse_cam) && b.cam_reversed) {
                // If camera hasn't been changed by something else, change it back to normal camera
                if (StartOfRound.Instance.activeCamera == b.backcam) {
                    StartOfRound.Instance.SwitchCamera(___gameplayCamera);
                    __instance.thisPlayerModelArms.enabled = true;
                    __instance.thisPlayerModel.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    b.backcam.enabled = false;
                    b.cam_reversed = false;
                }
            }

            // Debug output
            if (keys.CamInfoKey.triggered) {
                b.LogCameraInfo(StartOfRound.Instance.activeCamera);
            }
        }
    }

    [HarmonyPatch(typeof(StartOfRound))]
    internal class GetLayerMaskPatch {
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void StartPostfix(ref int ___collidersAndRoomMask) {
            // This layer mask will be used when detecting surfaces the reverse camera should "push up against" i.e. walls and solid objects
            LCBackCamBase.instance.layer_mask = ___collidersAndRoomMask;

            // Reset values that are set at round start
            LCBackCamBase.instance.backcam = null;
            LCBackCamBase.instance.cam_reversed = false;
        }
    }
}