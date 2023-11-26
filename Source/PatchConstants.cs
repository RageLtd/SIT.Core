using BepInEx.Logging;
using Comfort.Common;
using EFT;
using FilesChecker;
using HarmonyLib;
using Newtonsoft.Json;
using SIT.Core.Misc;
using SIT.Tarkov.Core.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine.Assertions;
using static SIT.Core.Misc.PaulovJsonConverters;

namespace SIT.Tarkov.Core
{
    public static class PatchConstants
    {
        public static BindingFlags PrivateFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static Type[] _eftTypes;
        public static Type[] EftTypes
        {
            get
            {
                if (_eftTypes == null)
                {
                    _eftTypes = typeof(AbstractGame).Assembly.GetTypes().OrderBy(t => t.Name).ToArray();
                }

                return _eftTypes;
            }
        }
        public static Type[] FilesCheckerTypes { get; private set; }
        public static Type LocalGameType { get; private set; }
        public static Type ExfilPointManagerType { get; private set; }
        public static Type BackendInterfaceType { get; private set; }
        public static Type SessionInterfaceType { get; private set; }

        public static Type StartWithTokenType { get; private set; }

        public static Type PoolManagerType { get; set; }

        public static Type JobPriorityType { get; set; }

        //public static Type PlayerInfoType { get; set; }
        //public static Type PlayerCustomizationType { get; set; }

        //public static Type SpawnPointSystemInterfaceType { get; set; }
        //public static Type SpawnPointArrayInterfaceType { get; set; }
        //public static Type SpawnPointSystemClassType { get; set; }

        //public static Type BackendStaticConfigurationType { get; set; }
        public static object BackendStaticConfigurationConfigInstance { get; set; }

        //public static class CharacterControllerSettings
        //{
        //    public static object CharacterControllerInstance { get; set; }
        //    public static CharacterControllerSpawner.Mode ObservedPlayerMode { get; set; }
        //    public static CharacterControllerSpawner.Mode ClientPlayerMode { get; set; }
        //    public static CharacterControllerSpawner.Mode BotPlayerMode { get; set; }
        //}


        /// <summary>
        /// A Key/Value dictionary of storing & obtaining an array of types by name
        /// </summary>
        public static readonly Dictionary<string, Type[]> TypesDictionary = new();

        /// <summary>
        /// A Key/Value dictionary of storing & obtaining a type by name
        /// </summary>
        public static Dictionary<string, Type> TypeDictionary { get; } = new();

        /// <summary>
        /// A Key/Value dictionary of storing & obtaining a method by type and name
        /// </summary>
        public static readonly Dictionary<(Type, string), MethodInfo> MethodDictionary = new();

        private static string backendUrl;
        /// <summary>
        /// Method that returns the Backend Url (Example: https://127.0.0.1)
        /// </summary>
        private static string RealWSURL;    //did i do this right?
        //It appears to be successful :D
        public static string GetBackendUrl()
        {
            if (string.IsNullOrEmpty(backendUrl))
            {
                backendUrl = BackendConnection.GetBackendConnection().BackendUrl;
            }
            return backendUrl;
        }
        public static string GetREALWSURL() //cut the server address obtained from GetBackendUrl and convert it to "ws://" w
        {
            if (string.IsNullOrEmpty(RealWSURL))
            {
                Logger.LogDebug("RealWSURL is null or empty");
                Logger.LogDebug("BackendConnection.GetBackendConnection().WebsocketUrl: " + BackendConnection.GetBackendConnection().WebsocketUrl);
                RealWSURL = BackendConnection.GetBackendConnection().WebsocketUrl;
                RealWSURL = RealWSURL.Replace("http", "ws");
            }
            Logger.LogDebug("RealWSURL: " + RealWSURL);
            return RealWSURL;
        }
        public static string GetPHPSESSID()
        {
            if (BackendConnection.GetBackendConnection() == null)
                Logger.LogError("Cannot get Backend Info");

            return BackendConnection.GetBackendConnection().PHPSESSID;
        }



        public static ManualLogSource Logger { get; private set; }

        //public static Type GroupingType { get; }
        public static Type JsonConverterType { get; }
        public static Newtonsoft.Json.JsonConverter[] JsonConverterDefault { get; }

        private static ISession _backEndSession;
        public static ISession BackEndSession
        {
            get
            {
                if (_backEndSession == null && Singleton<TarkovApplication>.Instantiated)
                {
                    _backEndSession = Singleton<TarkovApplication>.Instance.GetClientBackEndSession();
                }

                if (_backEndSession == null && Singleton<ClientApplication<ISession>>.Instantiated)
                {
                    _backEndSession = Singleton<ClientApplication<ISession>>.Instance.GetClientBackEndSession();
                }

                return _backEndSession;
            }
        }





        public static Newtonsoft.Json.JsonConverter[] GetJsonConvertersBSG()
        {
            return JsonConverterDefault;
        }

        public static List<Newtonsoft.Json.JsonConverter> GetJsonConvertersPaulov()
        {
            var converters = new List<Newtonsoft.Json.JsonConverter>();
            converters.Add(new DateTimeOffsetJsonConverter());
            converters.Add(new SimpleCharacterControllerJsonConverter());
            converters.Add(new CollisionFlagsJsonConverter());
            converters.Add(new PlayerJsonConverter());
            converters.Add(new NotesJsonConverter());
            return converters;
        }

        private static List<Newtonsoft.Json.JsonConverter> SITSerializerConverters;

        public static JsonSerializerSettings GetJsonSerializerSettings()
        {
            if (SITSerializerConverters == null || SITSerializerConverters.Count == 0)
            {
                SITSerializerConverters = GetJsonConvertersBSG().ToList();
                var paulovconverters = GetJsonConvertersPaulov();
                SITSerializerConverters.AddRange(paulovconverters.ToArray());
            }

            return new JsonSerializerSettings()
            {
                Converters = SITSerializerConverters,
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                FloatFormatHandling = FloatFormatHandling.DefaultValue,
                FloatParseHandling = FloatParseHandling.Double,
                Error = (serializer, err) =>
                {
                    Logger.LogError("SERIALIZATION ERROR");
                    Logger.LogError(err.ErrorContext.Error.ToString());
                }
            };
        }
        public static JsonSerializerSettings GetJsonSerializerSettingsWithoutBSG()
        {
            var converters = GetJsonConvertersPaulov();

            return new JsonSerializerSettings()
            {
                Converters = converters,
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Error = (serializer, err) =>
                {
                    Logger.LogError(err.ErrorContext.Error.ToString());
                }
            };
        }

        public static string SITToJson(this object o)
        {


            return JsonConvert.SerializeObject(o
                    , GetJsonSerializerSettings()
                );
        }

        public static async Task<string> SITToJsonAsync(this object o)
        {
            return await Task.Run(() =>
            {
                return SITToJson(o);
            });
        }

        public static T SITParseJson<T>(this string str)
        {
            return JsonConvert.DeserializeObject<T>(str
                    , GetJsonSerializerSettings()
                    );
        }

        public static bool TrySITParseJson<T>(this string str, out T result)
        {
            try
            {
                //result = JsonConvert.DeserializeObject<T>(str
                //        , new JsonSerializerSettings()
                //        {
                //            Converters = JsonConverterDefault
                //            ,
                //            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                //        }
                //        );
                result = SITParseJson<T>(str);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError(nameof(TrySITParseJson) + ": has filed to Parse Json");
                Logger?.LogError(nameof(TrySITParseJson) + ": " + str);
                Logger?.LogError(nameof(TrySITParseJson) + ": " + ex);
                result = default(T);
                return false;
            }
        }

        public static object GetPlayerProfile(object __instance)
        {
            var instanceProfile = __instance.GetType().GetProperty("Profile"
                , BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy).GetValue(__instance);
            if (instanceProfile == null)
            {
                Logger.LogInfo("ReplaceInPlayer:PatchPostfix: Couldn't find Profile");
                return null;
            }
            return instanceProfile;
        }

        public static string GetPlayerProfileAccountId(object instanceProfile)
        {
            var instanceAccountProp = instanceProfile.GetType().GetField("AccountId"
                , BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            if (instanceAccountProp == null)
            {
                Logger.LogInfo($"ReplaceInPlayer:PatchPostfix: instanceAccountProp not found");
                return null;
            }
            var instanceAccountId = instanceAccountProp.GetValue(instanceProfile).ToString();
            return instanceAccountId;
        }

        public static IDisposable StartWithToken(string name)
        {
            return ReflectionHelpers.GetAllMethodsForType(StartWithTokenType).Single(x => x.Name == "StartWithToken").Invoke(null, new object[] { name }) as IDisposable;
        }

        public static async Task InvokeAsyncStaticByReflection(MethodInfo methodInfo, object rModel, params object[] p)
        {
            if (rModel == null)
            {
                await (Task)methodInfo
                    .MakeGenericMethod(new[] { rModel.GetType() })
                    .Invoke(null, p);
            }
            else
            {
                await (Task)methodInfo
                    .Invoke(null, p);
            }
        }

        public static ClientApplication<ISession> GetClientApp()
        {
            return Singleton<ClientApplication<ISession>>.Instance;
        }

        public static TarkovApplication GetMainApp()
        {
            return GetClientApp() as TarkovApplication;
        }

        /// <summary>
        /// Invoke an async Task<object> method
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputType"></param>
        /// <param name="method"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        //public static async Task<object> InvokeAsyncMethod(Type type, Type outputType, string method, object[] param)
        //{
        //    var m = ReflectionHelpers.GetAllMethodsForType(type).First(x => x.Name == method);// foo.GetType().GetMethod(nameof(IFoo.Get));
        //    Logger.LogInfo("InvokeAsyncMethod." + m.Name);

        //    //var builder = AsyncTaskMethodBuilder.Create();

        //    var generic = m.MakeGenericMethod(outputType);
        //    var task = (Task)generic.Invoke(type, param);

        //    await task.ConfigureAwait(false);

        //    var resultProperty = task.GetType().GetProperty("Result");
        //    return resultProperty.GetValue(task);

        //}

        static PatchConstants()
        {
            if (Logger == null)
                Logger = BepInEx.Logging.Logger.CreateLogSource("SIT.Tarkov.Core.PatchConstants");

            TypesDictionary.Add("EftTypes", EftTypes);
            Logger.LogInfo($"PatchConstants: {EftTypes.Length} EftTypes found");

            FilesCheckerTypes = typeof(ICheckResult).Assembly.GetTypes();
            LocalGameType = EftTypes.Single(x => x.Name == "LocalGame");
            ExfilPointManagerType = EftTypes.Single(x => x.GetMethod("InitAllExfiltrationPoints") != null);
            BackendInterfaceType = EftTypes.Single(x => x.GetMethods().Select(y => y.Name).Contains("CreateClientSession") && x.IsInterface);
            SessionInterfaceType = EftTypes.Single(x => x.GetMethods().Select(y => y.Name).Contains("GetPhpSessionId") && x.IsInterface);
            DisplayMessageNotifications.MessageNotificationType = EftTypes.Single(x => x.GetMethods(BindingFlags.Static | BindingFlags.Public).Select(y => y.Name).Contains("DisplayMessageNotification"));
            //if (DisplayMessageNotifications.MessageNotificationType == null)
            //{
            //    Logger.LogInfo("SIT.Tarkov.Core:PatchConstants():MessageNotificationType:Not Found");
            //}
            //GroupingType = EftTypes.Single(x => x.GetMethods(BindingFlags.Public | BindingFlags.Static).Select(y => y.Name).Contains("CreateRaidPlayer"));
            //if (GroupingType != null)
            //{
            //  Logger.LogInfo("SIT.Tarkov.Core:PatchConstants():Found GroupingType:" + GroupingType.FullName);
            //}

            JsonConverterType = typeof(AbstractGame).Assembly.GetTypes()
               .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);
            JsonConverterDefault = JsonConverterType.GetField("Converters", BindingFlags.Static | BindingFlags.Public).GetValue(null) as JsonConverter[];
            Logger.LogInfo($"PatchConstants: {JsonConverterDefault.Length} JsonConverters found");

            StartWithTokenType = EftTypes.Single(x => ReflectionHelpers.GetAllMethodsForType(x).Count(y => y.Name == "StartWithToken") == 1);

            //BotSystemHelpers.Setup();

            if (JobPriorityType == null)
            {
                JobPriorityType = EftTypes.Single(x =>
                    ReflectionHelpers.GetAllMethodsForType(x).Any(x => x.Name == "Priority")
                    );
                //Logger.LogInfo($"Loading JobPriorityType:{JobPriorityType.FullName}");
            }

            //if (PlayerInfoType == null)
            //{
            //    PlayerInfoType = EftTypes.Single(x =>
            //        ReflectionHelpers.GetAllMethodsForType(x).Any(x => x.Name == "AddBan")
            //        && ReflectionHelpers.GetAllMethodsForType(x).Any(x => x.Name == "RemoveBan")
            //        );
            //    //Logger.LogInfo($"Loading PlayerInfoType:{PlayerInfoType.FullName}");
            //}

            //if (PlayerCustomizationType == null)
            //{
            //    PlayerCustomizationType = ReflectionHelpers.GetFieldFromType(typeof(Profile), "Customization").FieldType;
            //    //Logger.LogInfo($"Loading PlayerCustomizationType:{PlayerCustomizationType.FullName}");
            //}

            //SpawnPointArrayInterfaceType = EftTypes.Single(x =>
            //            ReflectionHelpers.GetAllMethodsForType(x).Any(x => x.Name == "CreateSpawnPoint")
            //            && ReflectionHelpers.GetAllMethodsForType(x).Any(x => x.Name == "DestroySpawnPoint")
            //            && x.IsInterface
            //        );
            //Logger.LogDebug($"Loading SpawnPointArrayInterfaceType:{SpawnPointArrayInterfaceType.FullName}");

            //BackendStaticConfigurationType = EftTypes.Single(x =>
            //        ReflectionHelpers.GetAllMethodsForType(x).Any(x => x.Name == "LoadApplicationConfig")
            //);


            // ==================== TEST ==========================
            // TODO: Replace with Unit Tests
            //Profile profile = new Profile();
            //profile.Info = new ProfileInfo() { Experience = 1 };
            //var pJson = profile.SITToJson();
            //Logger.LogDebug( pJson );
            //var pProfile = pJson.SITParseJson<Profile>();
            //Assert.AreEqual<Profile>(profile, pProfile);
        }
    }
}
