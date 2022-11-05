﻿using BepInEx;
using HarmonyLib;
using Aki.Reflection.Patching;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using EFT.InputSystem;
using Aki.Reflection.Utils;
using BepInEx.Logging;
using EFT.Animations;
using System.Runtime.InteropServices;
using System.Drawing;
using Aki.Common.Http;
using Aki.Common.Utils;
using System.IO;
using Newtonsoft.Json;
using System.Collections.Generic;
using BepInEx.Configuration;
using static RealismMod.Attributes;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace RealismMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<float> sensChangeRate { get; set; }
        public static ConfigEntry<float> sensResetRate { get; set; }
        public static ConfigEntry<float> sensLimit { get; set; }
        public static ConfigEntry<bool> showBalance { get; set; }
        public static ConfigEntry<bool> showCamRecoil { get; set; }
        public static ConfigEntry<bool> showDispersion { get; set; }
        public static ConfigEntry<bool> showRecoilAngle { get; set; }
        public static ConfigEntry<bool> showSemiROF { get; set; }
        public static ConfigEntry<bool> enableFSPatch { get; set; }
        public static ConfigEntry<bool> enableMalfPatch { get; set; }
        public static ConfigEntry<bool> enableSGMastering { get; set; }
        public static ConfigEntry<bool> enableProgramK { get; set; }

        public static float timer = 0.0f;
        public static bool isFiring = false;
        public static bool isAiming;
        public static bool statsReset;
        public static float shotCount = 0;
        public static float prevShotCount = shotCount;

        public static float startingRecoilAngle;

        public static float startingSens;
        public static float currentSens;

        public static float startingDispersion;
        public static float currentDispersion;
        public static float dispersionProportionK;

        public static float startingDamping;
        public static float currentDamping;
        public static float dampingProporitonK;

        public static float startingConvergence;
        public static float currentConvergence;
        public static float convergenceProporitonK;

        public static float startingCamRecoilX;
        public static float startingCamRecoilY;
        public static float currentCamRecoilX;
        public static float currentCamRecoilY;

        public static float startingVRecoilX;
        public static float startingVRecoilY;
        public static float currentVRecoilX;
        public static float currentVRecoilY;


        public static Dictionary<Enum, Sprite> iconCache = new Dictionary<Enum, Sprite>();
        public static string modPath;
        public static void CacheIcons()
        {
            iconCache.Add(ENewItemAttributeId.VerticalRecoil, Resources.Load<Sprite>("characteristics/icons/Ergonomics"));
            iconCache.Add(ENewItemAttributeId.HorizontalRecoil, Resources.Load<Sprite>("characteristics/icons/Recoil Back"));
            iconCache.Add(ENewItemAttributeId.Dispersion, Resources.Load<Sprite>("characteristics/icons/Velocity"));
            iconCache.Add(ENewItemAttributeId.CameraRecoil, Resources.Load<Sprite>("characteristics/icons/SightingRange"));
            iconCache.Add(ENewItemAttributeId.AutoROF, Resources.Load<Sprite>("characteristics/icons/bFirerate"));
            iconCache.Add(ENewItemAttributeId.SemiROF, Resources.Load<Sprite>("characteristics/icons/bFirerate"));
            iconCache.Add(ENewItemAttributeId.ReloadSpeed, Resources.Load<Sprite>("characteristics/icons/weapFireType"));
            iconCache.Add(ENewItemAttributeId.FixSpeed, Resources.Load<Sprite>("characteristics/icons/icon_info_raidmoddable"));
            iconCache.Add(ENewItemAttributeId.ChamberSpeed, Resources.Load<Sprite>("characteristics/icons/icon_info_raidmoddable"));
            iconCache.Add(ENewItemAttributeId.AimSpeed, Resources.Load<Sprite>("characteristics/icons/SightingRange"));
            _ = LoadTexture(ENewItemAttributeId.Balance, Path.Combine(modPath, "res\\balance.png"));
            _ = LoadTexture(ENewItemAttributeId.RecoilAngle, Path.Combine(modPath, "res\\recoilAngle.png"));
        }

        private void GetPath()
        {
            var mod = RequestHandler.GetJson($"/RealismMod/GetInfo");
            modPath = Json.Deserialize<string>(mod);
        }

        public static async Task LoadTexture(Enum id, string path)
        {
            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(path))
            {
                uwr.SendWebRequest();

                while (!uwr.isDone)
                    await Task.Delay(5);

                if (uwr.responseCode != 200)
                {
                }
                else
                {
                    // Get downloaded asset bundle
                    //Log.Info($"[{modName}] Retrieved texture! {id.ToString()} from {path}");
                    Texture2D cachedTexture = DownloadHandlerTexture.GetContent(uwr);
                    iconCache.Add(id, Sprite.Create(cachedTexture, new Rect(0, 0, cachedTexture.width, cachedTexture.height), new Vector2(0, 0)));
                }
            }
        }

        void Awake()
        {
            string RecoilSettings= "Recoil Settings";
            string WeapStatSettings = "Weapon Stat Settings";
            string MiscSettings = "Misc. Settigns";

            sensLimit = Config.Bind<float>(RecoilSettings, "Sensitivity Limit", 0.3f, new ConfigDescription("Sensitivity Lower Limit While Firing. Lower Means More Sensitivity Reduction.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 3 }));
            sensResetRate = Config.Bind<float>(RecoilSettings, "Senisitivity Reset Rate", 1.12f, new ConfigDescription("Rate At Which Sensitivity Recovers After Firing. Higher Means Faster Rate.", new AcceptableValueRange<float>(1.01f, 2f), new ConfigurationManagerAttributes { Order = 2 }));
            sensChangeRate = Config.Bind<float>(RecoilSettings, "Sensitivity Change Rate", 0.7f, new ConfigDescription("Rate At Which Sensitivity Is Reduced While Firing. Lower Means Faster Rate.", new AcceptableValueRange<float>(0.1f, 1f), new ConfigurationManagerAttributes { Order = 1 }));

            showBalance = Config.Bind<bool>(WeapStatSettings, "Show Balance Stat", true, new ConfigDescription("Requiures Restart. Warning: showing too many stats on weapons with lots of slots makes the inspect menu UI difficult to use.", null, new ConfigurationManagerAttributes { Order = 5 }));
            showCamRecoil = Config.Bind<bool>(WeapStatSettings, "Show Camera Recoil Stat", false, new ConfigDescription("Requiures Restart. Warning: showing too many stats on weapons with lots of slots makes the inspect menu UI difficult to use.", null, new ConfigurationManagerAttributes { Order = 4 }));
            showDispersion = Config.Bind<bool>(WeapStatSettings, "Show Dispersion Stat", false, new ConfigDescription("Requiures Restart. Warning: showing too many stats on weapons with lots of slots makes the inspect menu UI difficult to use.", null, new ConfigurationManagerAttributes { Order = 3 }));
            showRecoilAngle = Config.Bind<bool>(WeapStatSettings, "Show Recoil Angle Stat", true, new ConfigDescription("Requiures Restart. Warning: showing too many stats on weapons with lots of slots makes the inspect menu UI difficult to use.", null, new ConfigurationManagerAttributes { Order = 2 }));
            showSemiROF = Config.Bind<bool>(WeapStatSettings, "Show Semi Auto ROF Stat", true, new ConfigDescription("Requiures Restart. Warning: showing too many stats on weapons with lots of slots makes the inspect menu UI difficult to use.", null, new ConfigurationManagerAttributes { Order = 1 }));

            enableProgramK = Config.Bind<bool>(MiscSettings, "Enable ProgramK Compatibility", false, new ConfigDescription("Requires Restart. Emables integration of some ProgramK features. This option currently accomdates multiple buffer tube stock slots, withc each position modifiying stock stats differently.", null, new ConfigurationManagerAttributes { Order = 4 }));
            enableFSPatch = Config.Bind<bool>(MiscSettings, "Enable Faceshield Patch", true, new ConfigDescription("Faceshields block ADS unless the specfic Stock/Weapon/Faceshield allows it.", null, new ConfigurationManagerAttributes { Order = 3 }));
            enableMalfPatch = Config.Bind<bool>(MiscSettings, "Enable Malfuction Patch", true, new ConfigDescription("Requires Restart. You don't need to inspect a Malfunction in order to clear it.", null, new ConfigurationManagerAttributes { Order = 2 }));
            enableSGMastering = Config.Bind<bool>(MiscSettings, "Enable Increased Shotgun Mastery", true, new ConfigDescription("Requires Restart. Shotguns will get set to base lvl 2 mastery for reload animations, giving them better pump animations. ADS while reloading is unaffected.", null, new ConfigurationManagerAttributes { Order = 1 }));

            if (enableProgramK.Value == true)
            {
                Helper.ProgramKEnabled = true;
                Logger.LogInfo("Realism Mod: ProgramK Compatibiltiy Enabled!");
            }

            GetPath();
            CacheIcons();

            //Stat assignment patches
            new COIDeltaPatch().Enable();
            new TotalShotgunDispersionPatch().Enable();
            new GetDurabilityLossOnShotPatch().Enable();
            new AutoFireRatePatch().Enable();
            new SingleFireRatePatch().Enable();
            new ErgoDeltaPatch().Enable();
            new ErgoWeightPatch().Enable();

            new SyncWithCharacterSkillsPatch().Enable();
            new UpdateWeaponVariablesPatch().Enable();
            new SetAimingSlowdownPatch().Enable();

            new method_17Patch().Enable();
            new UpdateSwayFactorsPatch().Enable();

            //Recoil Patches
            new OnWeaponParametersChangedPatch().Enable();
            new UpdateSensitivityPatch().Enable();
            new AimingSensitivityPatch().Enable();
            new ProcessPatch().Enable();
            new ShootPatch().Enable();

            //Aiming Patches + Reload Trigger
            new AimingPatches().Enable();
            new ToggleAimPatch().Enable();

            //Malf Patches
            if (enableMalfPatch.Value == true)
            {
                new IsKnownMalfTypePatch().Enable();
            }

            //Reload Patches
            new CanStartReloadPatch().Enable();
            new ReloadMagPatch().Enable();
            new QuickReloadMagPatch().Enable();
            new ReloadWithAmmoPatch().Enable();
            new ReloadBarrelsPatch().Enable();
            new ReloadRevolverDrumPatch().Enable();

            new OnMagInsertedPatch().Enable();
            new SetMagTypeCurrentPatch().Enable();
            new SetMagTypeNewPatch().Enable();
            new SetMagInWeaponPatch().Enable();

            new RechamberSpeedPatch().Enable();
            new SetMalfRepairSpeedPatch().Enable();
            new SetBoltActionReloadPatch().Enable();
            new CheckChamberPatch().Enable();
            new SetSpeedParametersPatch().Enable();
            new CheckAmmoPatch().Enable();
            new SetHammerArmedPatch().Enable();

            if (enableSGMastering.Value == true)
            {
                new SetWeaponLevelPatch().Enable();
            }

            //Stat Display Patches
            new ModConstructorPatch().Enable();
            new WeaponConstructorPatch().Enable();
            new HRecoilDisplayValuePatch().Enable();
            new HRecoilDisplayDeltaPatch().Enable();
            new VRecoilDisplayValuePatch().Enable();
            new VRecoilDisplayDeltaPatch().Enable();
            new ModVRecoilStatDisplayPatch().Enable();
            new ModVRecoilStatDisplayPatch2().Enable();
            new ErgoDisplayDeltaPatch().Enable();
            new ErgoDisplayValuePatch().Enable();
            new COIDisplayDeltaPatch().Enable();
            new COIDisplayValuePatch().Enable();
            new FireRateDisplayStringPatch().Enable();

            new GetAttributeIconPatches().Enable();

        }


        void FixedUpdate()
        {
            if (Helper.checkIsReady())
            {
                Helper.isReady = true;
                if (isAiming == true)
                {
                    if (shotCount > prevShotCount)
                    {
                        if (shotCount >= 2 && shotCount <= 5)
                        {
                            if (currentVRecoilX < startingVRecoilX * WeaponProperties.vRecoilLimit)
                            {
                                currentVRecoilX *= 1.125f;
                                currentVRecoilY *= 1.125f;
                            }
                            if (currentConvergence > startingConvergence * WeaponProperties.convergenceLimit)
                            {
                                currentConvergence = Mathf.Min((convergenceProporitonK / currentVRecoilX), currentConvergence);
                            }
                        }
                        if (shotCount > 5 && shotCount <= 10)
                        {
                            if (currentVRecoilX < startingVRecoilX * WeaponProperties.vRecoilLimit)
                            {
                                currentVRecoilX *= 1.075f;
                                currentVRecoilY *= 1.075f;
                            }
                            if (currentConvergence > startingConvergence * WeaponProperties.convergenceLimit)
                            {
                                currentConvergence = Mathf.Min((convergenceProporitonK / currentVRecoilX), currentConvergence);
                            }
                        }

                        if (shotCount > 10 && shotCount <= 15)
                        {
                            if (currentVRecoilX < startingVRecoilX * WeaponProperties.vRecoilLimit)
                            {
                                currentVRecoilX *= 1.04f;
                                currentVRecoilY *= 1.04f;
                            }
                            if (currentConvergence > startingConvergence * WeaponProperties.convergenceLimit)
                            {
                                currentConvergence = Mathf.Min((convergenceProporitonK / currentVRecoilX), currentConvergence);
                            }
                            if (currentDamping > startingDamping * WeaponProperties.dampingLimit)
                            {
                                currentDamping *= 0.98f;
                            }
                        }

                        if (shotCount > 15 && shotCount <= 20)
                        {
                            if (currentVRecoilX < startingVRecoilX * WeaponProperties.vRecoilLimit)
                            {
                                currentVRecoilX *= 1.02f;
                                currentVRecoilY *= 1.02f;
                            }
                            if (currentConvergence > startingConvergence * WeaponProperties.convergenceLimit)
                            {
                                currentConvergence = Mathf.Min((convergenceProporitonK / currentVRecoilX), currentConvergence);
                            }
                            if (currentDamping > startingDamping * WeaponProperties.dampingLimit)
                            {
                                currentDamping *= 0.98f;
                            }
                        }

                        if (shotCount > 20 && shotCount <= 25)
                        {
                            if (currentVRecoilX < startingVRecoilX * WeaponProperties.vRecoilLimit)
                            {
                                currentVRecoilX *= 1.02f;
                                currentVRecoilY *= 1.02f;
                            }
                            if (currentConvergence > startingConvergence * WeaponProperties.convergenceLimit)
                            {
                                currentConvergence = Mathf.Min((convergenceProporitonK / currentVRecoilX), currentConvergence);
                            }
                            if (currentDamping > startingDamping * WeaponProperties.dampingLimit)
                            {
                                currentDamping *= 0.99f;
                            }
                        }

                        if (shotCount > 25 && shotCount <= 30)
                        {
                            if (currentVRecoilX < startingVRecoilX * WeaponProperties.vRecoilLimit)
                            {
                                currentVRecoilX *= 1.02f;
                                currentVRecoilY *= 1.02f;
                            }
                            if (currentConvergence > startingConvergence * WeaponProperties.convergenceLimit)
                            {
                                currentConvergence = Mathf.Min((convergenceProporitonK / currentVRecoilX), currentConvergence);
                            }
                            if (currentDamping > startingDamping * WeaponProperties.dampingLimit)
                            {
                                currentDamping *= 0.99f;
                            }
                        }

                        if (shotCount > 30 && shotCount <= 35)
                        {
                            if (currentVRecoilX < startingVRecoilX * WeaponProperties.vRecoilLimit)
                            {
                                currentVRecoilX *= 1.02f;
                                currentVRecoilY *= 1.02f;
                            }
                            if (currentConvergence > startingConvergence * WeaponProperties.convergenceLimit)
                            {
                                currentConvergence = Mathf.Min((convergenceProporitonK / currentVRecoilX), currentConvergence);
                            }
                            if (currentDamping > startingDamping * WeaponProperties.dampingLimit)
                            {
                                currentDamping *= 0.99f;
                            }
                        }

                        if (shotCount > 35)
                        {
                            if (currentVRecoilX < startingVRecoilX * WeaponProperties.vRecoilLimit)
                            {
                                currentVRecoilX *= 1.02f;
                                currentVRecoilY *= 1.02f;
                            }
                            if (currentConvergence > startingConvergence * WeaponProperties.convergenceLimit)
                            {
                                currentConvergence = Mathf.Min((convergenceProporitonK / currentVRecoilX), currentConvergence);
                            }
                            if (currentDamping > startingDamping * WeaponProperties.dampingLimit)
                            {
                                currentDamping *= 0.995f;
                            }
                        }



                        if (currentSens > startingSens * sensLimit.Value)
                        {
                            currentSens *= sensChangeRate.Value;
                        }
                        if (currentCamRecoilX > startingCamRecoilX * WeaponProperties.camRecoilLimit)
                        {
                            currentCamRecoilX *= WeaponProperties.camRecoilChangeRate;
                            currentCamRecoilY *= WeaponProperties.camRecoilChangeRate;
                        }
                        prevShotCount = shotCount;
                        isFiring = true;
                    }
                }
                else
                {
                    if (shotCount > prevShotCount)
                    {
                        prevShotCount = shotCount;
                        isFiring = true;
                    }
                }


                if (shotCount == prevShotCount)
                {
                    timer += Time.deltaTime;
                    if (timer >= 0.15f)
                    {
                        isFiring = false;
                        shotCount = 0;
                        prevShotCount = 0;
                        timer = 0f;
                    }
                }

                if (isFiring == false)
                {
                    if (startingSens <= currentSens && startingConvergence <= currentConvergence && startingVRecoilX >= currentVRecoilX)
                    {
                        statsReset = true;
                    }
                    else
                    {
                        statsReset = false;
                    }
                }

                if (statsReset == false && isFiring == false)
                {
                    if (startingSens > currentSens)
                    {
                        currentSens *= sensResetRate.Value;
                    }
                    if (startingSens > currentSens)
                    {
                        currentSens *= sensResetRate.Value;
                    }
                    if (startingConvergence > currentConvergence)
                    {
                        currentConvergence *= WeaponProperties.convergenceResetRate;
                    }
                    if (startingDamping > currentDamping)
                    {
                        currentDamping *= WeaponProperties.dampingResetRate;
                    }
                    if (startingCamRecoilX > currentCamRecoilX)
                    {
                        currentCamRecoilX *= WeaponProperties.camRecoilResetRate;
                        currentCamRecoilY *= WeaponProperties.camRecoilResetRate;
                    }
                    if (startingVRecoilX < currentVRecoilX)
                    {
                        currentVRecoilX *= WeaponProperties.vRecoilResetRate;
                        currentVRecoilY *= WeaponProperties.vRecoilResetRate;
                    }
                }
            }
            else
            {
                Helper.isReady = false;
            }
        }
    }
}

