﻿using Newtonsoft.Json;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SIT.Core.Coop.NetworkPacket
{
    public abstract class BasePacket : ISITPacket
    {
        [JsonIgnore]
        static Random Randomizer { get; } = new Random();

        [JsonProperty(PropertyName = "serverId")]
        public string ServerId { get; set; } = CoopGameComponent.GetServerId();

        [JsonIgnore]
        private string _t;

        [JsonProperty(PropertyName = "t")]
        public string TimeSerializedBetter
        {
            get
            {
                if (string.IsNullOrEmpty(_t))
                    _t = DateTime.Now.Ticks.ToString("G");

                return _t;
            }
            set
            {
                _t = value;
            }
        }

        private double? _token;

        [JsonProperty(PropertyName = "tkn")]
        public double Token
        {
            get { return _token.HasValue ? _token.Value : Randomizer.NextDouble(); }
            set { _token = value; }
        }


        [JsonProperty(PropertyName = "m")]
        public virtual string Method { get; set; } = null;

        //[JsonProperty(PropertyName = "pong")]
        //public virtual string Pong { get; set; } = DateTime.UtcNow.Ticks.ToString("G");

        public BasePacket()
        {
            ServerId = CoopGameComponent.GetServerId();
        }

        public static PropertyInfo[] GetPropertyInfos(ISITPacket packet)
        {
            var allProps = ReflectionHelpers.GetAllPropertiesForObject(packet);
            var allPropsFiltered = allProps
              .Where(x => x.Name != "ServerId" && x.Name != "Method" && x.Name != "Randomizer")
              .OrderByDescending(x => x.Name == "ProfileId").ToArray();
            return allPropsFiltered;
        }

        public virtual string Serialize()
        {
            if (string.IsNullOrEmpty(ServerId))
            {
                throw new ArgumentNullException(nameof(ServerId));
            }

            if (string.IsNullOrEmpty(Method))
            {
                throw new ArgumentNullException(nameof(Method));
            }

            using BinaryWriter binaryWriter = new(new MemoryStream());
            binaryWriter.WriteNonPrefixedString("SIT"); // 3
            binaryWriter.WriteNonPrefixedString(ServerId); // pmc + 24 chars
            binaryWriter.WriteNonPrefixedString(Method); // Unknown
            binaryWriter.WriteNonPrefixedString("?");

            var allPropsFiltered = GetPropertyInfos(this);

            for (var i =0; i < allPropsFiltered.Count(); i++)
            {
                var prop = allPropsFiltered[i];
                binaryWriter.WriteNonPrefixedString(prop.GetValue(this).ToString());
                if(i != allPropsFiltered.Count() - 1)   
                    binaryWriter.WriteNonPrefixedString(",");
            }
            return Encoding.UTF8.GetString(((MemoryStream)binaryWriter.BaseStream).ToArray());
        }

        public virtual byte[] SerializeCompressed()
        {
            var str = $"SITC{Serialize()}";
            return Zlib.Compress(Encoding.UTF8.GetBytes(str), ZlibCompression.Normal);
        }

        public virtual ISITPacket Deserialize(byte[] bytes)
        {
            return this;
        }

    }

    public interface ISITPacket
    {
        public string ServerId { get; set; }
        public string TimeSerializedBetter { get; set; }
        public string Method { get; set; }

    }

    public static class SerializerExtensions
    {
        public static void WriteNonPrefixedString(this BinaryWriter binaryWriter, string value)
        {
            binaryWriter.Write(Encoding.UTF8.GetBytes(value));
        }

        public static T DeserializePacketSIT<T>(this T obj, string serializedPacket)
        {
            var separatedPacket = serializedPacket.Split(',');
            var index = 0;

            foreach (var prop in BasePacket.GetPropertyInfos((ISITPacket)obj))
            {
                switch (prop.PropertyType.Name)
                {
                    case "Float":
                        prop.SetValue(obj, float.Parse(separatedPacket[index].ToString()));
                        break;
                    case "Single":
                        prop.SetValue(obj, Single.Parse(separatedPacket[index].ToString()));
                        break;
                    case "Boolean":
                        prop.SetValue(obj, Boolean.Parse(separatedPacket[index].ToString()));
                        break;
                    case "String":
                        prop.SetValue(obj, separatedPacket[index]);
                        break;
                    case "Integer":
                    case "Int":
                    case "Int32":
                        prop.SetValue(obj, int.Parse(separatedPacket[index].ToString()));
                        break;
                    case "Double":
                        prop.SetValue(obj, double.Parse(separatedPacket[index].ToString()));
                        break;
                    case "Byte":
                        prop.SetValue(obj, byte.Parse(separatedPacket[index].ToString()));
                        break;
                    default:
                        PatchConstants.Logger.LogError($"{prop.Name} of type {prop.PropertyType.Name} could not be parsed by SIT Deserializer!");
                        break;
                }
                index++;
            }
            return obj;
        }
    }
}
