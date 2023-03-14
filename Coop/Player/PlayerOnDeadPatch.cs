﻿using EFT;
using SIT.Coop.Core.Web;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System.Collections.Generic;
using System.Reflection;

namespace SIT.Coop.Core.Player
{
    /// <summary>
    /// This on dead patch removes people from the CoopGameComponent Players list
    /// </summary>
    public class PlayerOnDeadPatch : ModulePatch
    {
        public PlayerOnDeadPatch(BepInEx.Configuration.ConfigFile config)
        {
        }

        protected override MethodBase GetTargetMethod() => ReflectionHelpers.GetMethodForType(typeof(EFT.Player), "OnDead");

        [PatchPostfix]
        public static void PatchPostfix(EFT.Player __instance, EDamageType damageType)
        {
            //if (CoopGameComponent.Players != null)
            //    CoopGameComponent.Players.TryRemove(__instance.Profile.AccountId, out _);

            Dictionary<string, object> dictionary = new Dictionary<string, object>();
            dictionary.Add("m", "Dead");
            ServerCommunication.PostLocalPlayerData(__instance, dictionary, out string returnedData, out var generatedDict);

        }
    }
}
