using LocalWhale.Core.Updates;

namespace LocalWhale.Core.Tests;

public sealed class FrontendBootHtmlValidationTests
{
    private const string LegacyHtml = """
        <!doctype html>
        <html lang="zh-CN"><head><title>DeepSeek Harness</title></head>
        <body>
          <script>window.__DSH_BOOT__ = {"modules":["@deepseek-ai/dsh-client-runtime","@deepseek-ai/dsh-client-ui-cordis"]};</script>
          <div id="root"></div>
        </body></html>
        """;

    private const string ViteAppShellHtml = """
        <!doctype html>
        <html lang="en">
          <head><meta charset="utf-8" /><title>DeepSeek Harness</title>
            <script type="module" crossorigin src="/assets/index-ClqxG24t.js"></script>
          </head>
          <body>
            <div id="root"></div>
          </body>
        </html>
        """;

    [Fact]
    public void Accepts_the_legacy_inline_boot_manifest_generation()
    {
        PnpmRuntimeInstaller.ValidateFrontendBootHtml(LegacyHtml);
    }

    [Fact]
    public void Accepts_the_vite_app_shell_generation_served_by_harness_0_1_1()
    {
        PnpmRuntimeInstaller.ValidateFrontendBootHtml(ViteAppShellHtml);
    }

    [Theory]
    [InlineData("<html><body>Service unavailable</body></html>")]
    [InlineData("<html><body><script>window.__DSH_BOOT__ = {};</script></body></html>")]
    [InlineData("<html><body><div id=\"root\"></div></body></html>")]
    public void Rejects_pages_without_a_boot_manifest_or_app_shell(string html)
    {
        Assert.Throws<InvalidDataException>(() => PnpmRuntimeInstaller.ValidateFrontendBootHtml(html));
    }
}
