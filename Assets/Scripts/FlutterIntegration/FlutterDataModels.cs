using System;

namespace FlutterIntegration
{
    // Extend this file with new classes as your payload types grow

    [Serializable]
    public class UserProfilePayload
    {
        public string userName;
        public int noorCoins;
        public string profileImagePath; // Use local path or URL, avoid Base64
    }

    [Serializable]
    public class CoinUpdatePayload
    {
        public int newBalance;
    }
}
