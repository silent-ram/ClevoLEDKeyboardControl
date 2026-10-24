using ColorfulLedKeyboard.Installer;

namespace ColorfulLedKeyboard.Tests;

/// <summary>安装器双语文案表：中英 key 必须对齐且非空，防止漏翻。</summary>
public class InstallerStringsTests
{
    [Fact]
    public void AllKeys_HaveBothLanguages_NonEmpty()
    {
        Assert.NotEmpty(InstallerStrings.AllKeys);
        foreach (var key in InstallerStrings.AllKeys)
        {
            InstallerStrings.Set(InstallerStrings.ZhCn);
            var zh = InstallerStrings.Get(key);
            InstallerStrings.Set(InstallerStrings.EnUs);
            var en = InstallerStrings.Get(key);

            Assert.False(string.IsNullOrWhiteSpace(zh), $"{key} 缺少中文文案");
            Assert.False(string.IsNullOrWhiteSpace(en), $"{key} 缺少英文文案");
            Assert.NotEqual(key, zh);
            Assert.NotEqual(key, en);
        }
    }

    [Fact]
    public void Set_UnknownLanguage_FallsBackToChinese()
    {
        InstallerStrings.Set(InstallerStrings.EnUs);
        Assert.True(InstallerStrings.IsEnglish);
        InstallerStrings.Set("fr-FR");
        Assert.False(InstallerStrings.IsEnglish);
        InstallerStrings.Set(InstallerStrings.ZhCn);
    }
}
