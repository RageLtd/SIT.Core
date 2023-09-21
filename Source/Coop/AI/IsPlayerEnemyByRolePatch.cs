﻿using EFT;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System.Reflection;

namespace SIT.Core.Coop.AI
{
    internal class IsPlayerEnemyByRolePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelpers.GetMethodForType(typeof(BotGroupClass), "IsPlayerEnemyByRole");
        }

        //[PatchPrefix]
        //public static bool Prefix(
        //    bool __result,
        //    WildSpawnType role
        //    )
        //{
        //    __result = true;
        //    return true;
        //}

        [PatchPostfix]
        public static void Postfix(
            bool __result,
            WildSpawnType role,
            BotGroupClass __instance
            )
        {
            __result = true;
        }
    }
}
