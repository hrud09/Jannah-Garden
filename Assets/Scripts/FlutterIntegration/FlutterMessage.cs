using System;

namespace FlutterIntegration
{
    [Serializable]
    public class FlutterMessage
    {
        public string command;
        public string data; // Contains a stringified JSON of the actual payload
    }
}
