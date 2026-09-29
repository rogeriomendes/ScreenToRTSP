using System.Collections.Generic;
using System.Globalization;

namespace ScreenToRTSP.I18n
{
    public static class LanguageManager
    {
        public static Dictionary<string, string> Current;
        public static string CurrentLangCode = "System";
        public static event System.Action OnLanguageChanged;

        public static Dictionary<string, Dictionary<string, string>> AvailableLanguages = new Dictionary<string, Dictionary<string, string>>
        {
            { "pt-BR", pt_BR.Strings },
            { "es-ES", es_ES.Strings },
            { "en-US", en_US.Strings }
        };

        public static List<string> GetLanguageKeys()
        {
            var list = new List<string> { "System" };
            list.AddRange(AvailableLanguages.Keys);
            return list;
        }

        static LanguageManager()
        {
            ApplyLanguage("System");
        }

        public static void ApplyLanguage(string langCode)
        {
            CurrentLangCode = langCode;
            if (langCode == "System" || string.IsNullOrEmpty(langCode))
            {
                string sysLang = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLower();
                if (sysLang == "pt" && AvailableLanguages.ContainsKey("pt-BR")) Current = AvailableLanguages["pt-BR"];
                else if (sysLang == "es" && AvailableLanguages.ContainsKey("es-ES")) Current = AvailableLanguages["es-ES"];
                else Current = AvailableLanguages["en-US"];
            }
            else
            {
                if (AvailableLanguages.ContainsKey(langCode)) Current = AvailableLanguages[langCode];
                else Current = AvailableLanguages["en-US"];
            }
            
            OnLanguageChanged?.Invoke();
        }

        public static string Get(string key)
        {
            return Current != null && Current.ContainsKey(key) ? Current[key] : key;
        }
    }
}
