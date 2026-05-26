using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;
using System.Collections.Generic;

namespace CS2_Metro
{
    [FileLocation(nameof(CS2_Metro))]
    [SettingsUIGroupOrder(kExportGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kExportGroup, kDebugGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kExportGroup = "Export";
        public const string kDebugGroup = "Debug";

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kExportGroup)]
        [SettingsUIButton]
        public bool ExportTestMetroJson
        {
            set
            {
                TestMetroJsonExporter.ExportTestMetroJson();
            }
        }

        [SettingsUISection(kSection, kExportGroup)]
        [SettingsUIButton]
        public bool ExportRealMetroJson
        {
            set
            {
                RealMetroJsonExporter.ExportRealMetroJson(Mod.UpdateSystem);
            }
        }

        [SettingsUISection(kSection, kDebugGroup)]
        [SettingsUIButton]
        public bool ExportTransportDebugDump
        {
            set
            {
                TransportDebugDumpExporter.ExportDebugDump(Mod.UpdateSystem);
            }
        }

        public override void SetDefaults()
        {
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "CS2 Metro Diagram" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kExportGroup), "Export" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Debug" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportTestMetroJson)), "Export Test Metro JSON" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportTestMetroJson)), "Writes a static sample metro.json file for testing the offline SVG pipeline." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportRealMetroJson)), "Export Real Metro JSON" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportRealMetroJson)), "Writes a narrow real metro export from current CS2 transport line data. No SVG preview is generated in-game." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportTransportDebugDump)), "Export Transport Debug Dump" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportTransportDebugDump)), "Writes transport-related ECS diagnostics for manual analysis. This does not export a real metro diagram." }
            };
        }

        public void Unload()
        {
        }
    }
}
