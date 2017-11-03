using Newtonsoft.Json;
using System.IO;

namespace Tests {
    public class Credentials {
        [JsonProperty("host")]
        public string Host { get; private set; }

        [JsonProperty("username")]
        public string Username { get; private set; }

        [JsonProperty("password")]
        public string Password { get; private set; }

        private static Credentials _Current;
        public static Credentials Current {
            get {
                if (null == _Current) {
                    _Current = JsonConvert.DeserializeObject<Credentials>(File.ReadAllText("credentials.json"));
                }

                return _Current;
            }
        }
    }
}
