const resources = new Map();
let nextHandle = 1;
let canvas = null;
let context = null;
let device = null;
let swapchainFormat = null;
let adapterInfo = "";
let encoder = null;
let pass = null;
let activeTarget = null;
let activeTargetFormat = null;
let fallbackTexture = null;
let fallbackTextureView = null;
let fallbackSamplers = new Map();
let pipelineCache = new Map();
let pipelineKeys = new WeakMap();
let pipelineLabels = new WeakMap();
let textureBindGroupCache = new Map();
// Breadcrumb describing the draw currently being recorded, surfaced by the
// uncapturederror handler so async WebGPU validation errors can be traced back
// to the shader/material that triggered them.
let lastDraw = "(none)";
let pendingClears = new Map();
let uniformRing = null;
let retiredUniformBuffers = [];
let retiredBuffers = [];
let bufferResources = new Set();
let pendingSubmissions = 0;
let computePipelineCache = new Map();
let downloadSlots = new Map();
let mouseX = 0;
let mouseY = 0;
let mouseWheelX = 0;
let mouseWheelY = 0;
const mouseButtonsDown = [false, false, false];
const keysDown = new Set();
const uniformOffsetAlignment = 256;
const uniformSizeAlignment = 16;
const initialUniformRingSize = 1024 * 1024;

function add(resource) {
	const handle = nextHandle++;
	resources.set(handle, resource);
	return handle;
}

function get(handle) {
	const resource = resources.get(handle);
	if (!resource)
		throw new Error(`Invalid Foster WebGPU resource ${handle}`);
	return resource;
}

function textureFormat(format) {
	switch (format) {
		case 1: return "r8unorm";
		case 2: return "rg8unorm";
		default: return "rgba8unorm";
	}
}

function bytesPerPixelForFormat(format) {
	switch (format) {
		case "r8unorm": return 1;
		case "rg8unorm": return 2;
		default: return 4;
	}
}

function vertexFormat(type, normalized) {
	switch (type) {
		case 1: return "float32";
		case 2: return "float32x2";
		case 3: return "float32x3";
		case 4: return "float32x4";
		case 5: return normalized ? "snorm8x4" : "sint8x4";
		case 6: return normalized ? "unorm8x4" : "uint8x4";
		case 7: return normalized ? "snorm16x2" : "sint16x2";
		case 8: return normalized ? "unorm16x2" : "uint16x2";
		case 9: return normalized ? "snorm16x4" : "sint16x4";
		case 10: return normalized ? "unorm16x4" : "uint16x4";
		default: throw new Error(`Unsupported Foster vertex type ${type}`);
	}
}

function vertexLayout(stride, locations, offsets, types, normalized, elementCount) {
	const attributes = [];
	for (let i = 0; i < elementCount; i++) {
		attributes.push({
			shaderLocation: locations[i],
			offset: offsets[i],
			format: vertexFormat(types[i], normalized[i] !== 0),
		});
	}
	return {
		arrayStride: stride,
		attributes,
	};
}

function vertexLayoutKey(stride, locations, offsets, types, normalized, elementCount) {
	let key = `${stride}`;
	for (let i = 0; i < elementCount; i++)
		key += `:${locations[i]},${offsets[i]},${types[i]},${normalized[i]}`;
	return key;
}

function blendOperation(value) {
	return ["add", "subtract", "reverse-subtract", "min", "max"][value] ?? "add";
}

function blendFactor(value) {
	return [
		"zero",
		"one",
		"src",
		"one-minus-src",
		"dst",
		"one-minus-dst",
		"src-alpha",
		"one-minus-src-alpha",
		"dst-alpha",
		"one-minus-dst-alpha",
		"constant",
		"one-minus-constant",
		"src-alpha-saturated",
	][value] ?? "one";
}

function colorWriteMask(mask) {
	let result = 0;
	if ((mask & 1) !== 0) result |= GPUColorWrite.RED;
	if ((mask & 2) !== 0) result |= GPUColorWrite.GREEN;
	if ((mask & 4) !== 0) result |= GPUColorWrite.BLUE;
	if ((mask & 8) !== 0) result |= GPUColorWrite.ALPHA;
	return result;
}

function createPipeline(vertexShader, fragmentShader, targetFormat, blend, vertexStride, vertexLocations, vertexOffsets, vertexTypes, vertexNormalized, vertexElementCount, vertexLayoutCacheKey) {
	const key = `${vertexShader}:${fragmentShader}:${targetFormat}:${blend.join(",")}:${vertexLayoutCacheKey}`;
	const cached = pipelineCache.get(key);
	if (cached)
		return cached;

	const label = `${get(vertexShader).name} -> ${get(fragmentShader).name}`;
	const pipeline = device.createRenderPipeline({
		label,
		layout: "auto",
		vertex: {
			module: get(vertexShader).module,
			entryPoint: get(vertexShader).entryPoint,
			buffers: [vertexLayout(vertexStride, vertexLocations, vertexOffsets, vertexTypes, vertexNormalized, vertexElementCount)],
		},
		fragment: {
			module: get(fragmentShader).module,
			entryPoint: get(fragmentShader).entryPoint,
			targets: [{
				format: targetFormat,
				blend: {
					color: {
						srcFactor: blendFactor(blend[1]),
						dstFactor: blendFactor(blend[2]),
						operation: blendOperation(blend[0]),
					},
					alpha: {
						srcFactor: blendFactor(blend[4]),
						dstFactor: blendFactor(blend[5]),
						operation: blendOperation(blend[3]),
					},
				},
				writeMask: colorWriteMask(blend[6]),
			}],
		},
		primitive: { topology: "triangle-list" },
	});

	pipelineCache.set(key, pipeline);
	pipelineKeys.set(pipeline, key);
	pipelineLabels.set(pipeline, label);
	return pipeline;
}

function alignTo(value, alignment) {
	return (value + alignment - 1) & ~(alignment - 1);
}

function writeBuffer(data, usage) {
	const bytes = data instanceof Uint8Array ? data : new Uint8Array(data);
	const alignment = (usage & GPUBufferUsage.UNIFORM) !== 0 ? 16 : 4;
	const buffer = device.createBuffer({
		size: Math.max(alignment, (bytes.byteLength + alignment - 1) & ~(alignment - 1)),
		usage,
		mappedAtCreation: true,
	});
	new Uint8Array(buffer.getMappedRange()).set(bytes);
	buffer.unmap();
	return buffer;
}

function ensureUniformRing(requiredSize) {
	const size = alignTo(Math.max(requiredSize, initialUniformRingSize), uniformOffsetAlignment);
	if (uniformRing && uniformRing.size >= size)
		return;

	if (uniformRing?.buffer)
		retiredUniformBuffers.push(uniformRing.buffer);

	uniformRing = {
		buffer: device.createBuffer({
			label: "Foster Uniform Ring",
			size,
			usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
		}),
		size,
		offset: 0,
		generation: (uniformRing?.generation ?? 0) + 1,
		bindGroups: new Map(),
	};
}

function uniformBytes(data, byteLength) {
	const bytes = data instanceof Uint8Array ? data : new Uint8Array(data);
	return byteLength === bytes.byteLength ? bytes : bytes.subarray(0, byteLength);
}

function writeUniform(data, byteLength, pipeline, groupIndex) {
	if (byteLength <= 0)
		return null;

	const bytes = uniformBytes(data, byteLength);
	const bindingSize = alignTo(byteLength, uniformSizeAlignment);
	const slotSize = alignTo(bindingSize, uniformOffsetAlignment);
	ensureUniformRing(slotSize);
	if (uniformRing.offset + slotSize > uniformRing.size)
		ensureUniformRing(Math.max(slotSize, uniformRing.size * 2));

	const offset = uniformRing.offset;
	uniformRing.offset += slotSize;
	device.queue.writeBuffer(uniformRing.buffer, offset, bytes);

	const pipelineKey = pipelineKeys.get(pipeline) ?? "unknown";
	const key = `${uniformRing.generation}:${pipelineKey}:${groupIndex}:${offset}:${bindingSize}`;
	let bindGroup = uniformRing.bindGroups.get(key);
	if (!bindGroup) {
		const kind = groupIndex === 1 ? "vertex-uniform" : groupIndex === 3 ? "fragment-uniform" : `uniform@group${groupIndex}`;
		bindGroup = device.createBindGroup({
			label: `${pipelineLabels.get(pipeline) ?? "unknown"} [${kind}, ${bindingSize}B]`,
			layout: pipeline.getBindGroupLayout(groupIndex),
			entries: [{ binding: 0, resource: { buffer: uniformRing.buffer, offset, size: bindingSize } }],
		});
		uniformRing.bindGroups.set(key, bindGroup);
	}
	return bindGroup;
}

function textureCacheKey(textureHandle, filter, wrapX, wrapY) {
	return (((textureHandle || 0) * 3 + (filter ?? 0)) * 3 + (wrapX ?? 0)) * 3 + (wrapY ?? 0);
}

function textureBindGroupFor(pipeline, textureHandles, samplerFilters, samplerWrapX, samplerWrapY, samplerCount) {
	let node = textureBindGroupCache.get(pipeline);
	if (!node) {
		node = { children: new Map(), bindGroup: null };
		textureBindGroupCache.set(pipeline, node);
	}

	for (let i = 0; i < samplerCount; i++) {
		const key = textureCacheKey(textureHandles[i], samplerFilters[i], samplerWrapX[i], samplerWrapY[i]);
		let child = node.children.get(key);
		if (!child) {
			child = { children: new Map(), bindGroup: null };
			node.children.set(key, child);
		}
		node = child;
	}

	if (node.bindGroup)
		return node.bindGroup;

	const entries = [];
	for (let i = 0; i < samplerCount; i++) {
		const textureView = textureHandles[i] ? get(textureHandles[i]).view : fallbackTextureView;
		entries.push({ binding: i, resource: textureView });
		entries.push({ binding: 100 + i, resource: samplerFor(samplerFilters[i] ?? 0, samplerWrapX[i] ?? 0, samplerWrapY[i] ?? 0) });
	}
	node.bindGroup = device.createBindGroup({
		label: `${pipelineLabels.get(pipeline) ?? "unknown"} [textures x${samplerCount}]`,
		layout: pipeline.getBindGroupLayout(2),
		entries,
	});
	return node.bindGroup;
}

function samplerFor(filter, wrapX, wrapY) {
	const key = `${filter}:${wrapX}:${wrapY}`;
	const cached = fallbackSamplers.get(key);
	if (cached)
		return cached;

	const wrap = value => value === 1 ? "repeat" : value === 2 ? "mirror-repeat" : "clamp-to-edge";
	const mode = filter === 1 ? "linear" : "nearest";
	const sampler = device.createSampler({
		minFilter: mode,
		magFilter: mode,
		addressModeU: wrap(wrapX),
		addressModeV: wrap(wrapY),
	});
	fallbackSamplers.set(key, sampler);
	return sampler;
}

function targetInfo(targetHandle) {
	if (targetHandle === 0) {
		return {
			handle: 0,
			width: canvas.width,
			height: canvas.height,
			format: swapchainFormat,
			view: context.getCurrentTexture().createView(),
		};
	}

	const target = get(targetHandle);
	const attachment = get(target.attachments[0]);
	return {
		handle: targetHandle,
		width: target.width,
		height: target.height,
		format: attachment.format,
		view: attachment.view,
	};
}

function ensureEncoder() {
	if (!encoder)
		encoder = device.createCommandEncoder();
}

function endPass() {
	if (pass) {
		pass.end();
		pass = null;
		activeTarget = null;
		activeTargetFormat = null;
	}
}

function flushPendingClear(targetHandle) {
	if (!pendingClears.has(targetHandle))
		return;

	endPass();
	ensurePass(targetHandle);
	endPass();
}

function flushPendingClearForTexture(textureHandle) {
	if (!textureHandle)
		return;

	const texture = get(textureHandle);
	// Clears are deferred until a target is drawn into. If the target is only
	// sampled this frame, force the clear first so stale pixels do not leak.
	if (texture.targetHandle)
		flushPendingClear(texture.targetHandle);
}

function ensurePass(targetHandle) {
	const info = targetInfo(targetHandle);
	if (pass && activeTarget === targetHandle && activeTargetFormat === info.format)
		return info;

	endPass();
	ensureEncoder();

	const clear = pendingClears.get(targetHandle);
	pendingClears.delete(targetHandle);
	pass = encoder.beginRenderPass({
		colorAttachments: [{
			view: info.view,
			loadOp: clear ? "clear" : "load",
			clearValue: clear ?? { r: 0, g: 0, b: 0, a: 0 },
			storeOp: "store",
		}],
	});
	activeTarget = targetHandle;
	activeTargetFormat = info.format;
	return info;
}

export function getAdapterInfo() {
	return adapterInfo;
}

export async function initialize(canvasSelector) {
	if (!navigator.gpu)
		throw new Error("WebGPU is not available in this browser.");

	canvas = document.querySelector(canvasSelector) ?? document.querySelector("canvas");
	if (!canvas)
		throw new Error(`Canvas '${canvasSelector}' was not found.`);

	const adapter = await navigator.gpu.requestAdapter();
	if (!adapter)
		throw new Error("WebGPU adapter was not available.");
	// Browsers may blank some fields for privacy; keep whatever they report.
	const info = adapter.info ?? {};
	adapterInfo = [info.vendor, info.architecture, info.device, info.description].filter(Boolean).join(" ");

	device = await adapter.requestDevice();
	device.addEventListener("uncapturederror", event => {
		const error = event.error;
		const kind = error?.constructor?.name ?? "GPUError";
		console.error(`WebGPU ${kind} during draw [${lastDraw}]:`, error?.message ?? error);
	});
	context = canvas.getContext("webgpu");
	swapchainFormat = navigator.gpu.getPreferredCanvasFormat();
	context.configure({ device, format: swapchainFormat, alphaMode: "premultiplied" });
	samplerFor(0, 0, 0);
	samplerFor(1, 0, 0);

	const pixel = new Uint8Array([255, 255, 255, 255]);
	fallbackTexture = device.createTexture({
		size: [1, 1],
		format: "rgba8unorm",
		usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST,
	});
	fallbackTextureView = fallbackTexture.createView();
	device.queue.writeTexture({ texture: fallbackTexture }, pixel, { bytesPerRow: 4 }, [1, 1]);

	const updateMousePosition = event => {
		const rect = canvas.getBoundingClientRect();
		mouseX = (event.clientX - rect.left) * canvas.width / rect.width;
		mouseY = (event.clientY - rect.top) * canvas.height / rect.height;
	};

	canvas.addEventListener("mousemove", updateMousePosition);
	canvas.addEventListener("mousedown", event => {
		updateMousePosition(event);
		if (event.button >= 0 && event.button < mouseButtonsDown.length)
			mouseButtonsDown[event.button] = true;
		event.preventDefault();
	});
	window.addEventListener("mouseup", event => {
		if (event.button >= 0 && event.button < mouseButtonsDown.length)
			mouseButtonsDown[event.button] = false;
	});
	canvas.addEventListener("wheel", event => {
		mouseWheelX -= event.deltaX / 100;
		mouseWheelY -= event.deltaY / 100;
		event.preventDefault();
	});
	canvas.addEventListener("contextmenu", event => event.preventDefault());

	// Map a single finger on the canvas to the left mouse button so pointer-based
	// UI (menus) works on touch devices. Managed code polls mouse state once per
	// frame; synthetic touch->mouse events fire down+up together at touch-end and
	// coalesce within a frame, so the press is missed. Driving the button off the
	// real touch lifecycle (down at touchstart, up at touchend) makes it stick.
	const updateTouchPosition = touch => {
		const rect = canvas.getBoundingClientRect();
		mouseX = (touch.clientX - rect.left) * canvas.width / rect.width;
		mouseY = (touch.clientY - rect.top) * canvas.height / rect.height;
	};
	let activeTouchId = null;
	canvas.addEventListener("touchstart", event => {
		if (activeTouchId === null) {
			const touch = event.changedTouches[0];
			activeTouchId = touch.identifier;
			updateTouchPosition(touch);
			mouseButtonsDown[0] = true;
		}
		event.preventDefault();
	}, { passive: false });
	canvas.addEventListener("touchmove", event => {
		for (const touch of event.changedTouches) {
			if (touch.identifier === activeTouchId) {
				updateTouchPosition(touch);
				event.preventDefault();
				break;
			}
		}
	}, { passive: false });
	const endTouch = event => {
		for (const touch of event.changedTouches) {
			if (touch.identifier === activeTouchId) {
				updateTouchPosition(touch);
				activeTouchId = null;
				mouseButtonsDown[0] = false;
				break;
			}
		}
	};
	canvas.addEventListener("touchend", endTouch);
	canvas.addEventListener("touchcancel", endTouch);

	window.addEventListener("keydown", event => keysDown.add(event.code));
	window.addEventListener("keyup", event => keysDown.delete(event.code));
}

export function ensureInitialized() {
	if (!device)
		throw new Error("Foster WebGPU was not initialized before managed startup.");
}

export function resizeCanvas(width, height) {
	canvas.width = width;
	canvas.height = height;
}

export function getCanvasWidth() {
	return canvas.width;
}

export function getCanvasHeight() {
	return canvas.height;
}

export function setTitle(title) {
	document.title = title;
}

export function setDebugOverlayStats(text) {
	globalThis.TinyLinkDebugOverlay?.setStats?.(text);
}

export function setMouseVisible(enabled) {
	canvas.style.cursor = enabled ? "default" : "none";
}

export function getMouseX() {
	return mouseX;
}

export function getMouseY() {
	return mouseY;
}

export function getMouseButtonDown(button) {
	return mouseButtonsDown[button === 1 ? 0 : button === 2 ? 1 : button === 3 ? 2 : -1] ?? false;
}

export function getMouseWheelX() {
	return mouseWheelX;
}

export function getMouseWheelY() {
	return mouseWheelY;
}

export function resetMouseWheel() {
	mouseWheelX = 0;
	mouseWheelY = 0;
}

export function isKeyDown(code) {
	return keysDown.has(code);
}

export function waitForAnimationFrame() {
	return new Promise(resolve => requestAnimationFrame(resolve));
}

export function startRenderLoop(tick) {
	function frame() {
		if (tick())
			requestAnimationFrame(frame);
	}
	requestAnimationFrame(frame);
}

function gamepadAt(slot) {
	return navigator.getGamepads?.()[slot] ?? null;
}

export function getGamepadSlotCount() {
	return navigator.getGamepads?.().length ?? 0;
}

export function getGamepadConnected(slot) {
	return gamepadAt(slot) !== null;
}

export function getGamepadId(slot) {
	return gamepadAt(slot)?.id ?? "";
}

export function getGamepadMapping(slot) {
	return gamepadAt(slot)?.mapping ?? "";
}

export function getGamepadButtonCount(slot) {
	return gamepadAt(slot)?.buttons.length ?? 0;
}

export function getGamepadAxisCount(slot) {
	return gamepadAt(slot)?.axes.length ?? 0;
}

export function getGamepadButtonPressed(slot, button) {
	return gamepadAt(slot)?.buttons[button]?.pressed ?? false;
}

export function getGamepadButtonValue(slot, button) {
	return gamepadAt(slot)?.buttons[button]?.value ?? 0;
}

export function getGamepadAxisValue(slot, axis) {
	return gamepadAt(slot)?.axes[axis] ?? 0;
}

export function createTarget(name, width, height) {
	return add({ name, width, height, attachments: [] });
}

export function createTexture(name, width, height, layers, formatValue, targetHandle, computeUsage = 0) {
	try {
		const layerCount = layers > 0 ? layers : 1;
		const format = textureFormat(formatValue);
		// COPY_SRC so any texture can be read back to the CPU via requestTextureDownload.
		let usage = GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST | GPUTextureUsage.COPY_SRC | GPUTextureUsage.RENDER_ATTACHMENT;
		if ((computeUsage & 3) !== 0)
			usage |= GPUTextureUsage.STORAGE_BINDING;
		const texture = device.createTexture({
			label: name,
			// depthOrArrayLayers > 1 makes this a layered texture; the "2d-array"
			// view below is what a texture_2d_array shader binding samples.
			size: [width, height, layerCount],
			format,
			usage,
		});
		// A layered texture must be viewed as "2d-array" to bind to a
		// texture_2d_array shader resource; a plain 2D texture keeps its default
		// "2d" view so ordinary sprite/target shaders continue to bind.
		const view = layerCount > 1
			? texture.createView({ dimension: "2d-array", arrayLayerCount: layerCount })
			: texture.createView();
		const handle = add({ texture, view, format, width, height, layers: layerCount, targetHandle });
		if (targetHandle) {
			const target = get(targetHandle);
			target.attachments.push(handle);
		}
		return handle;
	} catch (error) {
		console.error("createTexture failed:", name, error?.message ?? error, error?.stack ?? "");
		throw new Error(String(error?.message ?? error));
	}
}

export function uploadTexture(textureHandle, layer, data, x, y, width, height, bytesPerPixel) {
	flushPendingClearForTexture(textureHandle);
	const resource = get(textureHandle);
	const source = data instanceof Uint8Array ? data : new Uint8Array(data);
	const requiredBytesPerRow = width * bytesPerPixel;
	let upload = source;
	let bytesPerRow = requiredBytesPerRow;

	if (bytesPerRow % 256 !== 0 && height > 1) {
		bytesPerRow = (bytesPerRow + 255) & ~255;
		upload = new Uint8Array(bytesPerRow * height);
		for (let row = 0; row < height; row++) {
			upload.set(
				source.subarray(row * requiredBytesPerRow, row * requiredBytesPerRow + requiredBytesPerRow),
				row * bytesPerRow);
		}
	}

	// origin z / the third copy-size component select the array layer to write.
	device.queue.writeTexture(
		{ texture: resource.texture, origin: [x, y, layer] },
		upload,
		{ bytesPerRow },
		[width, height, 1]);
}

export function createShader(name, code, entryPoint) {
	return add({
		name: name || "(unnamed shader)",
		module: device.createShaderModule({ label: name, code }),
		entryPoint,
	});
}

export function createBuffer(name, usage, indexFormat = 1) {
	// indexFormat: 0 = 16-bit (ushort/short), 1 = 32-bit. Only meaningful for
	// index buffers; setIndexBuffer must match the mesh's index element size.
	const resource = { name, usage, buffer: null, buffers: [], capacity: 0, nextBuffer: 0, indexFormat: indexFormat === 0 ? "uint16" : "uint32" };
	const handle = add(resource);
	bufferResources.add(resource);
	return handle;
}

export function uploadBuffer(bufferHandle, data, dataDestOffset = 0, dataSize = data.byteLength, requiredSize = dataDestOffset + dataSize) {
	const resource = get(bufferHandle);
	const usage =
		(resource.usage === 1 ? GPUBufferUsage.VERTEX : 0) |
		(resource.usage === 2 ? GPUBufferUsage.INDEX : 0) |
		(resource.usage === 4 ? (GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC) : 0) |
		GPUBufferUsage.COPY_DST;
	const source = data instanceof Uint8Array ? data : new Uint8Array(data);
	const required = Math.max(4, requiredSize);

	if (resource.capacity < required) {
		for (const buffer of resource.buffers)
			retiredBuffers.push(buffer);
		resource.buffers = [];
		resource.capacity = alignTo(required, 4);
		resource.nextBuffer = 0;
	}

	let buffer = resource.buffers[resource.nextBuffer];
	if (!buffer) {
		buffer = device.createBuffer({
			label: resource.name,
			size: resource.capacity,
			usage,
		});
		resource.buffers.push(buffer);
	}
	resource.nextBuffer++;
	resource.buffer = buffer;

	if (dataSize > 0) {
		// WebGPU requires writeBuffer's size to be a multiple of 4. uint16 index buffers
		// (e.g. imgui, with an odd index count) can be 2-byte-misaligned, so pad the write
		// up; the buffer capacity is already aligned to 4 above. Without this the write is
		// rejected as a validation error and the device is lost (black screen after a frame).
		const writeSize = alignTo(dataSize, 4);
		let src = source;
		if (writeSize > source.byteLength) {
			src = new Uint8Array(writeSize);
			src.set(source.subarray(0, dataSize));
		}
		device.queue.writeBuffer(buffer, dataDestOffset, src, 0, writeSize);
	}
}

export function destroyResource(resourceHandle) {
	const resource = resources.get(resourceHandle);
	if (activeTarget === resourceHandle)
		endPass();
	pendingClears.delete(resourceHandle);
	if (resource?.attachments)
		for (const attachment of resource.attachments)
			destroyResource(attachment);
	if (resource?.buffers) {
		bufferResources.delete(resource);
		for (const buffer of resource.buffers)
			buffer.destroy();
	}
	if (resource?.texture)
		resource.texture.destroy();
	if (resource?.texture || resource?.attachments)
		textureBindGroupCache.clear();
	resources.delete(resourceHandle);
}

export function clearTarget(targetHandle, r, g, b, a) {
	if (pass && activeTarget === targetHandle)
		endPass();
	pendingClears.set(targetHandle, { r, g, b, a });
}

export function drawBatch(
	vertexShader,
	fragmentShader,
	vertexBuffer,
	indexBuffer,
	vertexStride,
	vertexLocations,
	vertexOffsets,
	vertexTypes,
	vertexNormalized,
	vertexElementCount,
	targetHandle,
	textureHandles,
	samplerFilters,
	samplerWrapX,
	samplerWrapY,
	samplerCount,
	vertexTextures,
	vertexSamplerFilters,
	vertexSamplerWrapX,
	vertexSamplerWrapY,
	vertexSamplerCount,
	vertexStorageBuffers,
	vertexUniform,
	vertexUniformLength,
	fragmentUniform,
	fragmentUniformLength,
	blendColorOperation,
	blendColorSource,
	blendColorDestination,
	blendAlphaOperation,
	blendAlphaSource,
	blendAlphaDestination,
	blendMask,
	viewportX,
	viewportY,
	viewportWidth,
	viewportHeight,
	scissorX,
	scissorY,
	scissorWidth,
	scissorHeight,
	indexOffset,
	indexCount,
	vertexOffset,
	instanceCount,
	width,
	height) {
	for (let i = 0; i < samplerCount; i++)
		flushPendingClearForTexture(textureHandles[i]);
	for (let i = 0; i < vertexSamplerCount; i++)
		flushPendingClearForTexture(vertexTextures[i]);

	const target = ensurePass(targetHandle);
	const blend = [
		blendColorOperation,
		blendColorSource,
		blendColorDestination,
		blendAlphaOperation,
		blendAlphaSource,
		blendAlphaDestination,
		blendMask,
	];
	// Set the breadcrumb from shader names before createPipeline, so a pipeline-
	// or shader-module-creation failure is attributed to this draw (not the
	// previous one).
	lastDraw = `${get(vertexShader).name} -> ${get(fragmentShader).name} (vtxUniform=${vertexUniformLength}B, fragUniform=${fragmentUniformLength}B, samplers=${samplerCount})`;
	const layoutKey = vertexLayoutKey(vertexStride, vertexLocations, vertexOffsets, vertexTypes, vertexNormalized, vertexElementCount);
	const pipeline = createPipeline(vertexShader, fragmentShader, target.format, blend, vertexStride, vertexLocations, vertexOffsets, vertexTypes, vertexNormalized, vertexElementCount, layoutKey);

	pass.pushDebugGroup(lastDraw);

	// Group 0: vertex-stage textures (binding i), samplers (binding 100+i), and
	// vertex storage buffers (offset after the textures). Used by instanced
	// shaders like the particle renderer; absent for ordinary batcher draws.
	if (vertexSamplerCount > 0 || vertexStorageBuffers.length > 0) {
		const entries = [];
		for (let i = 0; i < vertexSamplerCount; i++) {
			const view = vertexTextures[i] ? get(vertexTextures[i]).view : fallbackTextureView;
			entries.push({ binding: i, resource: view });
			entries.push({ binding: 100 + i, resource: samplerFor(vertexSamplerFilters[i] ?? 0, vertexSamplerWrapX[i] ?? 0, vertexSamplerWrapY[i] ?? 0) });
		}
		for (let i = 0; i < vertexStorageBuffers.length; i++)
			entries.push({ binding: vertexSamplerCount + i, resource: { buffer: get(vertexStorageBuffers[i]).buffer } });
		pass.setBindGroup(0, device.createBindGroup({ label: `${lastDraw} [vertex group0]`, layout: pipeline.getBindGroupLayout(0), entries }));
	}

	if (vertexUniformLength > 0) {
		const matrixBindGroup = writeUniform(vertexUniform, vertexUniformLength, pipeline, 1);
		pass.setBindGroup(1, matrixBindGroup);
	}

	if (samplerCount > 0) {
		pass.setBindGroup(2, textureBindGroupFor(pipeline, textureHandles, samplerFilters, samplerWrapX, samplerWrapY, samplerCount));
	}

	if (fragmentUniformLength > 0) {
		const fragmentBindGroup = writeUniform(fragmentUniform, fragmentUniformLength, pipeline, 3);
		pass.setBindGroup(3, fragmentBindGroup);
	}

	pass.setViewport(viewportX, viewportY, viewportWidth, viewportHeight, 0, 1);
	if (scissorWidth > 0 && scissorHeight > 0) {
		// WebGPU validates the scissor lies within the attachment and aborts the device
		// otherwise. Other backends (Metal/D3D/Vulkan) silently clamp, so clamp here too
		// to match — imgui clip rects in particular can round a pixel past the edge.
		const sx = Math.max(0, Math.min(scissorX, target.width));
		const sy = Math.max(0, Math.min(scissorY, target.height));
		const sw = Math.max(0, Math.min(scissorX + scissorWidth, target.width) - sx);
		const sh = Math.max(0, Math.min(scissorY + scissorHeight, target.height) - sy);
		pass.setScissorRect(sx, sy, sw, sh);
	} else {
		pass.setScissorRect(0, 0, target.width, target.height);
	}
	pass.setPipeline(pipeline);
	pass.setVertexBuffer(0, get(vertexBuffer).buffer);
	pass.setIndexBuffer(get(indexBuffer).buffer, get(indexBuffer).indexFormat ?? "uint32");
	pass.drawIndexed(indexCount, instanceCount, indexOffset, vertexOffset, 0);
	pass.popDebugGroup();
}

function computePipelineFor(shaderHandle) {
	let pipeline = computePipelineCache.get(shaderHandle);
	if (pipeline)
		return pipeline;

	const shader = get(shaderHandle);
	if (!shader)
		throw new Error(`computePipelineFor: invalid shader handle ${shaderHandle}`);
	pipeline = device.createComputePipeline({
		layout: "auto",
		compute: {
			module: shader.module,
			entryPoint: shader.entryPoint,
		},
	});
	computePipelineCache.set(shaderHandle, pipeline);
	pipelineKeys.set(pipeline, `compute:${shaderHandle}`);
	return pipeline;
}

export function dispatchCompute(
	shaderHandle,
	readOnlyTextures,
	samplerFilters,
	samplerWrapX,
	samplerWrapY,
	samplerCount,
	readOnlyBuffers,
	readWriteBuffers,
	readWriteTextures,
	uniformData,
	uniformLength,
	groupCountX,
	groupCountY,
	groupCountZ) {
	try {
		endPass();
		for (let i = 0; i < samplerCount; i++)
			flushPendingClearForTexture(readOnlyTextures[i]);
		ensureEncoder();

		lastDraw = `compute ${get(shaderHandle)?.name ?? shaderHandle}`;
		const pipeline = computePipelineFor(shaderHandle);
		const computePass = encoder.beginComputePass();
		computePass.setPipeline(pipeline);

		// Group 0 holds (in shader t-register order) sampled textures, then the
		// read-only storage buffers, plus samplers at binding 100+i — mirroring
		// the draw path. Textures occupy the low bindings, so buffers are offset.
		if (samplerCount > 0 || readOnlyBuffers.length > 0) {
			const entries = [];
			for (let i = 0; i < samplerCount; i++) {
				const view = readOnlyTextures[i] ? get(readOnlyTextures[i]).view : fallbackTextureView;
				entries.push({ binding: i, resource: view });
				entries.push({ binding: 100 + i, resource: samplerFor(samplerFilters[i] ?? 0, samplerWrapX[i] ?? 0, samplerWrapY[i] ?? 0) });
			}
			for (let i = 0; i < readOnlyBuffers.length; i++)
				entries.push({ binding: samplerCount + i, resource: { buffer: get(readOnlyBuffers[i]).buffer } });
			computePass.setBindGroup(0, device.createBindGroup({ label: `${lastDraw} [group0]`, layout: pipeline.getBindGroupLayout(0), entries }));
		}

		if (readWriteBuffers.length > 0 || readWriteTextures.length > 0) {
			const entries = [];
			for (let i = 0; i < readWriteBuffers.length; i++)
				entries.push({ binding: i, resource: { buffer: get(readWriteBuffers[i]).buffer } });
			for (let i = 0; i < readWriteTextures.length; i++)
				entries.push({ binding: readWriteBuffers.length + i, resource: get(readWriteTextures[i]).view });
			computePass.setBindGroup(1, device.createBindGroup({ layout: pipeline.getBindGroupLayout(1), entries }));
		}

		if (uniformLength > 0) {
			const bindGroup = writeUniform(uniformData, uniformLength, pipeline, 2);
			computePass.setBindGroup(2, bindGroup);
		}

		computePass.dispatchWorkgroups(groupCountX, groupCountY, groupCountZ);
		computePass.end();
	} catch (error) {
		console.error("dispatchCompute failed:", error?.message ?? error, error?.stack ?? "");
		throw new Error(String(error?.message ?? error));
	}
}

export function requestBufferDownload(bufferHandle, sourceOffsetBytes, lengthBytes, slot) {
	let entry = downloadSlots.get(slot);
	const size = Math.max(4, (lengthBytes + 3) & ~3);

	// A previous download is still being mapped; skip this request rather
	// than touching a buffer with an outstanding mapAsync call.
	if (entry?.mapPending)
		return;

	if (!entry || entry.size < size) {
		if (entry) {
			if (entry.mapped)
				entry.staging.unmap();
			entry.staging.destroy();
		}
		entry = {
			staging: device.createBuffer({ size, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST }),
			size,
			mapped: false,
			mapPending: false,
			pendingLength: undefined,
		};
		downloadSlots.set(slot, entry);
	} else if (entry.mapped) {
		entry.staging.unmap();
		entry.mapped = false;
	}

	ensureEncoder();
	encoder.copyBufferToBuffer(get(bufferHandle).buffer, sourceOffsetBytes, entry.staging, 0, lengthBytes);
	entry.pendingLength = lengthBytes;
}

export function tryReadBufferDownload(slot, length) {
	const entry = downloadSlots.get(slot);
	if (!entry || !entry.mapped)
		return null;

	const available = Math.min(length, entry.staging.size);
	return new Uint8Array(entry.staging.getMappedRange(0, available)).slice();
}

// Texture readback. copyTextureToBuffer requires each row to start on a
// 256-byte boundary, so the staging buffer holds padded rows; tryRead strips
// the padding back to a tightly-packed image. Shares the downloadSlots map (and
// thus present()'s mapAsync pump) with buffer downloads - slot ids are unique.
export function requestTextureDownload(textureHandle, x, y, width, height, bytesPerPixel, slot) {
	flushPendingClearForTexture(textureHandle);
	const texture = get(textureHandle);
	const rowBytes = width * bytesPerPixel;
	const paddedRowBytes = alignTo(rowBytes, 256);
	const size = paddedRowBytes * height;

	let entry = downloadSlots.get(slot);

	// A previous download is still being mapped; skip rather than touch a
	// buffer with an outstanding mapAsync call.
	if (entry?.mapPending)
		return;

	if (!entry || entry.size < size) {
		if (entry) {
			if (entry.mapped)
				entry.staging.unmap();
			entry.staging.destroy();
		}
		entry = {
			staging: device.createBuffer({ size, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST }),
			size,
			mapped: false,
			mapPending: false,
			pendingLength: undefined,
		};
		downloadSlots.set(slot, entry);
	} else if (entry.mapped) {
		entry.staging.unmap();
		entry.mapped = false;
	}

	entry.rowBytes = rowBytes;
	entry.paddedRowBytes = paddedRowBytes;
	entry.rowCount = height;

	ensureEncoder();
	endPass(); // copyTextureToBuffer cannot run inside a render pass
	encoder.copyTextureToBuffer(
		{ texture: texture.texture, origin: [x, y, 0] },
		{ buffer: entry.staging, bytesPerRow: paddedRowBytes, rowsPerImage: height },
		[width, height, 1]);
	entry.pendingLength = size;
}

export function tryReadTextureDownload(slot, length) {
	const entry = downloadSlots.get(slot);
	if (!entry || !entry.mapped || entry.rowBytes === undefined)
		return null;

	const mapped = new Uint8Array(entry.staging.getMappedRange());
	const tight = new Uint8Array(entry.rowBytes * entry.rowCount);
	for (let row = 0; row < entry.rowCount; row++) {
		const src = row * entry.paddedRowBytes;
		tight.set(mapped.subarray(src, src + entry.rowBytes), row * entry.rowBytes);
	}

	const available = Math.min(length, tight.length);
	return available === tight.length ? tight : tight.slice(0, available);
}

function submitEncoder() {
	if (encoder) {
		device.queue.submit([encoder.finish()]);
		encoder = null;
		pendingSubmissions++;
		device.queue.onSubmittedWorkDone().then(() => {
			pendingSubmissions--;
			if (pendingSubmissions === 0)
				for (const resource of bufferResources)
					resource.nextBuffer = 0;
		});
		for (const entry of downloadSlots.values()) {
			if (entry.pendingLength === undefined)
				continue;
			entry.pendingLength = undefined;
			entry.mapPending = true;
			entry.staging.mapAsync(GPUMapMode.READ).then(() => {
				entry.mapped = true;
				entry.mapPending = false;
			});
		}
	}
	if (uniformRing)
		uniformRing.offset = 0;
	if (retiredUniformBuffers.length > 0) {
		const buffers = retiredUniformBuffers;
		retiredUniformBuffers = [];
		device.queue.onSubmittedWorkDone().then(() => {
			for (const buffer of buffers)
				buffer.destroy();
		});
	}
	if (retiredBuffers.length > 0) {
		const buffers = retiredBuffers;
		retiredBuffers = [];
		device.queue.onSubmittedWorkDone().then(() => {
			for (const buffer of buffers)
				buffer.destroy();
		});
	}
}

export function submit() {
	endPass();
	submitEncoder();
}

export function present() {
	endPass();
	if (pendingClears.has(0))
		ensurePass(0);
	endPass();
	submitEncoder();
}
