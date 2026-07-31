namespace Foster.Framework;

internal class InputProviderHeadless : InputProvider
{
	public override void SetClipboard(string text) { }
	public override string GetClipboard() => string.Empty;
	public override void Rumble(ControllerID id, float lowIntensity, float highIntensity, float duration) { }
}
