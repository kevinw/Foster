#if BROWSER
namespace Foster.Framework;

internal static class BrowserDocument
{
	public static void SetTitle(string title)
		=> BrowserWebGPU.SetTitle(title);
}
#endif
