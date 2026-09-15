#if BROWSER
using System.Runtime.InteropServices.JavaScript;

namespace Foster.Framework;

internal static partial class BrowserWebGPU
{
	[JSImport("ensureInitialized", "FosterWebGPU")]
	internal static partial void Initialize(string canvasSelector);

	[JSImport("resizeCanvas", "FosterWebGPU")]
	internal static partial void ResizeCanvas(int width, int height);

	[JSImport("getCanvasWidth", "FosterWebGPU")]
	internal static partial int GetCanvasWidth();

	[JSImport("getCanvasHeight", "FosterWebGPU")]
	internal static partial int GetCanvasHeight();

	[JSImport("setTitle", "FosterWebGPU")]
	internal static partial void SetTitle(string title);

	[JSImport("setMouseVisible", "FosterWebGPU")]
	internal static partial void SetMouseVisible(bool enabled);

	[JSImport("getMouseX", "FosterWebGPU")]
	internal static partial float GetMouseX();

	[JSImport("getMouseY", "FosterWebGPU")]
	internal static partial float GetMouseY();

	[JSImport("getMouseButtonDown", "FosterWebGPU")]
	internal static partial bool GetMouseButtonDown(int button);

	[JSImport("getMouseWheelX", "FosterWebGPU")]
	internal static partial float GetMouseWheelX();

	[JSImport("getMouseWheelY", "FosterWebGPU")]
	internal static partial float GetMouseWheelY();

	[JSImport("resetMouseWheel", "FosterWebGPU")]
	internal static partial void ResetMouseWheel();

	[JSImport("isKeyDown", "FosterWebGPU")]
	internal static partial bool IsKeyDown(string code);

	[JSImport("waitForAnimationFrame", "FosterWebGPU")]
	internal static partial Task WaitForAnimationFrame();

	[JSImport("startRenderLoop", "FosterWebGPU")]
	internal static partial void StartRenderLoop(
		[JSMarshalAs<JSType.Function<JSType.Boolean>>] Func<bool> tick);

	[JSImport("getGamepadSlotCount", "FosterWebGPU")]
	internal static partial int GetGamepadSlotCount();

	[JSImport("getGamepadConnected", "FosterWebGPU")]
	internal static partial bool GetGamepadConnected(int slot);

	[JSImport("getGamepadId", "FosterWebGPU")]
	internal static partial string GetGamepadId(int slot);

	[JSImport("getGamepadMapping", "FosterWebGPU")]
	internal static partial string GetGamepadMapping(int slot);

	[JSImport("getGamepadButtonCount", "FosterWebGPU")]
	internal static partial int GetGamepadButtonCount(int slot);

	[JSImport("getGamepadAxisCount", "FosterWebGPU")]
	internal static partial int GetGamepadAxisCount(int slot);

	[JSImport("getGamepadButtonPressed", "FosterWebGPU")]
	internal static partial bool GetGamepadButtonPressed(int slot, int button);

	[JSImport("getGamepadButtonValue", "FosterWebGPU")]
	internal static partial float GetGamepadButtonValue(int slot, int button);

	[JSImport("getGamepadAxisValue", "FosterWebGPU")]
	internal static partial float GetGamepadAxisValue(int slot, int axis);

	[JSImport("createTexture", "FosterWebGPU")]
	internal static partial int CreateTexture(string name, int width, int height, int layers, int format, int target, int computeUsage);

	[JSImport("uploadTexture", "FosterWebGPU")]
	internal static partial void UploadTexture(int texture, int layer, byte[] data, int x, int y, int width, int height, int bytesPerPixel);

	[JSImport("createTarget", "FosterWebGPU")]
	internal static partial int CreateTarget(string name, int width, int height);

	[JSImport("createShader", "FosterWebGPU")]
	internal static partial int CreateShader(string name, string code, string entryPoint);

	[JSImport("createBuffer", "FosterWebGPU")]
	internal static partial int CreateBuffer(string name, int usage, int indexFormat);

	[JSImport("uploadBuffer", "FosterWebGPU")]
	internal static partial void UploadBuffer(int buffer, byte[] data, int dataDestOffset, int dataSize, int requiredSize);

	[JSImport("destroyResource", "FosterWebGPU")]
	internal static partial void DestroyResource(int resource);

	[JSImport("clearTarget", "FosterWebGPU")]
	internal static partial void ClearTarget(int target, float r, float g, float b, float a);

	[JSImport("drawBatch", "FosterWebGPU")]
	internal static partial void DrawBatch(
		int vertexShader,
		int fragmentShader,
		int vertexBuffer,
		int indexBuffer,
		int vertexStride,
		int[] vertexLocations,
		int[] vertexOffsets,
		int[] vertexTypes,
		int[] vertexNormalized,
		int vertexElementCount,
		int target,
		int[] textures,
		int[] samplerFilters,
		int[] samplerWrapX,
		int[] samplerWrapY,
		int samplerCount,
		int[] vertexTextures,
		int[] vertexSamplerFilters,
		int[] vertexSamplerWrapX,
		int[] vertexSamplerWrapY,
		int vertexSamplerCount,
		int[] vertexStorageBuffers,
		byte[] vertexUniform,
		int vertexUniformLength,
		byte[] fragmentUniform,
		int fragmentUniformLength,
		int blendColorOperation,
		int blendColorSource,
		int blendColorDestination,
		int blendAlphaOperation,
		int blendAlphaSource,
		int blendAlphaDestination,
		int blendMask,
		int viewportX,
		int viewportY,
		int viewportWidth,
		int viewportHeight,
		int scissorX,
		int scissorY,
		int scissorWidth,
		int scissorHeight,
		int indexOffset,
		int indexCount,
		int vertexOffset,
		int instanceCount,
		int width,
		int height);

	[JSImport("present", "FosterWebGPU")]
	internal static partial void Present();

	[JSImport("dispatchCompute", "FosterWebGPU")]
	internal static partial void DispatchCompute(
		int shader,
		int[] readOnlyTextures,
		int[] samplerFilters,
		int[] samplerWrapX,
		int[] samplerWrapY,
		int samplerCount,
		int[] readOnlyBuffers,
		int[] readWriteBuffers,
		int[] readWriteTextures,
		byte[] uniformData,
		int uniformLength,
		int groupCountX,
		int groupCountY,
		int groupCountZ);

	[JSImport("requestBufferDownload", "FosterWebGPU")]
	internal static partial void RequestBufferDownload(int buffer, int sourceOffsetBytes, int lengthBytes, int slot);

	[JSImport("tryReadBufferDownload", "FosterWebGPU")]
	internal static partial byte[]? TryReadBufferDownload(int slot, int length);

	[JSImport("requestTextureDownload", "FosterWebGPU")]
	internal static partial void RequestTextureDownload(int texture, int x, int y, int width, int height, int bytesPerPixel, int slot);

	[JSImport("tryReadTextureDownload", "FosterWebGPU")]
	internal static partial byte[]? TryReadTextureDownload(int slot, int length);
}
#endif
