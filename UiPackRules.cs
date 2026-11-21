using System;
using System.IO;
namespace CustomSoundtrack
{
    public static class UiPackRules
    {
        public const string Vanilla="Default (Vanilla)", Legacy="Legacy folders";
        public static string Stem(string key) => key.Substring(key.LastIndexOf('/')+1);
        public static bool AudioFile(string file) => string.Equals(Path.GetExtension(file),".wav",StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(file),".ogg",StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(file),".mp3",StringComparison.OrdinalIgnoreCase);
    }
}
