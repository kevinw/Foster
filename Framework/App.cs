using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if !BROWSER
using static SDL3.SDL;
#endif

namespace Foster.Framework;

/// <summary>
/// Application Information struct, to be provided to <seealso cref="App(in AppConfig)"/>
/// </summary>
/// <param name="ApplicationName">Application Name used for storing data and representing the Application</param>
/// <param name="WindowTitle">What to display in the Window Title</param>
/// <param name="Width">The Window Width</param>
/// <param name="Height">The Window Height</param>
/// <param name="Fullscreen">If the Window should default to Fullscreen</param>
/// <param name="Resizable">If the Window should be resizable</param>
/// <param name="UpdateMode">An optional default Update Mode to initialize the App with</param>
/// <param name="PreferredGraphicsDriver">The preferred graphics driver, or None to use the platform-default</param>
/// <param name="Flags">Optional App Initialization Flags</param>
/// <param name="FramesInFlight">Maximum frames that may be pending on the GPU, from 1 to 3</param>
public readonly record struct AppConfig
(
	string ApplicationName,
	string WindowTitle,
	int Width,
	int Height,
	bool Fullscreen = false,
	bool Resizable = true,
	UpdateMode? UpdateMode = null,
	GraphicsDriver PreferredGraphicsDriver = GraphicsDriver.None,
	AppFlags Flags = AppFlags.None,
	int FramesInFlight = 3
);

/// <summary>
/// Application-level events that will be notified through <see cref="App.OnEvent"/>
/// </summary>
public enum AppEvents
{
	/// <summary>
	/// When the Application enters the Background (usually on mobile devices)
	/// </summary>
	EnterBackground,

	/// <summary>
	/// When the Application enters the Foreground (usually on mobile devices)
	/// </summary>
	EnterForeground
}

/// <summary>
/// App Initialization Flags
/// </summary>
[Flags]
public enum AppFlags
{
	None = 0,

	/// <summary>
	/// Enabled Graphics Debugging properties and validation
	/// </summary>
	GraphicsDebugging = 1 << 0,

	/// <summary>
	/// Enables MultiSampling of the BackBuffer
	/// </summary>
	MultiSampledBackBuffer = 1 << 1,

	/// <summary>
	/// Doesn't log Foster's header (version number, gpu, SDL version, etc)
	/// </summary>
	NoHeaderLog = 1 << 2,

	/// <summary>
	/// Run the Application in Headless mode, without a Window or GPU device.
	/// </summary>
	Headless = 1 << 3,

	// Creates a GPU device without a Window or swapchain, for offscreen rendering tools.
	Offscreen = 1 << 4,

	/// <summary>
	/// Treat the Window Width/Height as logical sizes: on platforms that measure windows in
	/// pixels (Windows, X11), grow them by the display's content scale. No effect on macOS.
	/// </summary>
	ScaleWindowToDisplay = 1 << 5,
}

/// <summary>
/// Inherit the App with your game.<br/>
/// Call <see cref="Run"/> to begin the main game update loop.<br/>
/// Note you can only have one App running at a time.
/// </summary>
public abstract partial class App : IDisposable
{
	/// <summary>
	/// Foster Version Number
	/// </summary>
	public static readonly Version FosterVersion = typeof(App).Assembly.GetName().Version!;

	/// <summary>
	/// The Application Name
	/// </summary>
	public string Name => config.ApplicationName;

	/// <summary>
	/// Timing Module
	/// </summary>
	public Time Time { get; private set; }

	/// <summary>
	/// The real elapsed time since the Application Started
	/// </summary>
	public TimeSpan Now => timer.Elapsed;

	/// <summary>
	/// How the Application should update
	/// </summary>
	public UpdateMode UpdateMode;

	/// <summary>
	/// The Main Application Window.
	/// This will be <c>null</c> in Headless or Offscreen mode.
	/// </summary>
	public Window? Window { get; private set; }

	/// <summary>
	/// All Windows open in the Application
	/// </summary>
	public readonly ReadOnlyCollection<Window> Windows;

	/// <summary>
	/// The Input Module
	/// </summary>
	public readonly Input Input;

	/// <summary>
	/// The GPU Rendering Module
	/// </summary>
	public readonly GraphicsDevice GraphicsDevice;

	/// <summary>
	/// The FileSystem Module
	/// </summary>
	public readonly FileSystem FileSystem;

	/// <summary>
	/// If the Application is currently running
	/// </summary>
	public bool Running { get; private set; } = false;

	/// <summary>
	/// If the Application is exiting. Call <see cref="Exit"/> to exit the Application.
	/// </summary>
	public bool Exiting { get; private set; } = false;

	/// <summary>
	/// If the Application is Disposed
	/// </summary>
	public bool Disposed { get; private set; } = false;

	/// <summary>
	/// When an Application Event is called.
	/// Note that these are not necessarilyc called from the main thread.
	/// </summary>
	public event Action<AppEvents>? OnEvent;

	/// <summary>
	/// Gets the path to the User Directory, which is the location where you should
	/// store application data like settings or save data.<br/>
	/// <br/>
	/// The location of this directory is platform and application dependent.
	/// For example on Windows this is usually in `C:/User/{user}/AppData/Roaming/{App.Name}`,
	/// where as on Linux it's usually in `~/.local/share/{App.Name}`.<br/>
	/// <br/>
	/// Note that while using <see cref="System.IO"/> operations on the UserPath is generally
	/// supported across desktop environments, certain platforms require more explicit operations
	/// to mount and read/write data.<br/>
	/// <br/>
	/// If you intend to target non-desktop platforms, you should implement user data
	/// through the <see cref="FileSystem.OpenUserStorage(Action{ContentStorage})"/> API via <see cref="FileSystem"/>
	/// </summary>
	public string UserPath
	{
		get
		{
			userPath ??= UserPathForApplicationName(config.ApplicationName);
			return userPath;
		}
	}

	public static string UserPathForApplicationName(string applicationName) {
		#if BROWSER
			return $"/{applicationName}";
		#else
			// only assign the user path if requested - calling this method may
			// create a directory and we only want to do that if the user
			// actually intends to use it.
			return SDL_GetPrefPath(string.Empty, applicationName);
		#endif
	}

	/// <summary>
	/// What action to perform when the user requests for the Application to exit.<br/>
	/// If not assigned, the default behavior will call <see cref="Exit"/>.<br/>
	/// There is also <seealso cref="Window.OnCloseRequested"/> which will be called if the Window close button is pressed.
	/// </summary>
	public Action? OnExitRequested;

	private readonly AppConfig config;
	/// <summary>
	/// The number of simulation steps to run per render frame
	/// when in Unthrottled Mode with graphics enabled.
	/// Defaults to 60 (≈60× real-time at 60 FPS vsync).
	/// </summary>
	public int UnthrottledStepsPerFrame { get; set; } = 60;
	private readonly Stopwatch timer = new();
	private TimeSpan lastUpdateTime;
	private TimeSpan fixedAccumulator;
	private readonly int mainThreadID;
	private readonly ConcurrentQueue<Action> mainThreadQueue = [];
	private readonly InputProvider inputProvider;
	private string? userPath = null;
#if !BROWSER
	private readonly SDL_EventFilter eventFilter;
#endif
	private readonly List<Window> windows = [];
	private readonly Queue<Window> windowDestroyingQueue = [];
	private Cursor? currentCursor;
#if BROWSER
	private static App? browserRunningApp;
	private bool browserStarted;
#endif

	internal readonly Exception NotRunningException = new("The Application is not Running");
	internal readonly Exception DisposedException = new("The Application is Disposed");

	public App(string name, int width, int height)
		: this(new(name, name, width, height)) {}

	public unsafe App(in AppConfig config)
	{
		this.config = config;
		Windows = new(windows);

		if (string.IsNullOrEmpty(config.ApplicationName) || string.IsNullOrWhiteSpace(config.ApplicationName))
			throw new Exception("Invalid Application Name");

		// log info
		if (!config.Flags.Has(AppFlags.NoHeaderLog))
		{
			var startupLogs = new List<string>();
			startupLogs.Add($"Foster: v{FosterVersion.Major}.{FosterVersion.Minor}.{FosterVersion.Build}");
#if BROWSER
			startupLogs.Add("Browser WASM");
#else
			if (!config.Flags.Has(AppFlags.Headless))
			{
				var sdlv = SDL_GetVersion();
				startupLogs.Add($"SDL: v{sdlv / 1000000}.{(sdlv / 1000) % 1000}.{sdlv % 1000}");
			}
			startupLogs.Add($"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
#endif
			startupLogs.Add($"{RuntimeInformation.FrameworkDescription}");
			Log.Info(string.Join(" | ", startupLogs));
		}


		mainThreadID = Environment.CurrentManagedThreadId;

		if (config.Flags.Has(AppFlags.Headless))
		{
			UpdateMode = config.UpdateMode ?? UpdateMode.UnlockedStep();
			inputProvider = new InputProviderHeadless();
			Input = inputProvider.Input;
			FileSystem = new(this);
			GraphicsDevice = new GraphicsDeviceHeadless(this);
			GraphicsDevice.CreateDevice(config.Flags);
			Window = null;
#if !BROWSER
			eventFilter = default!;
#endif
		}
#if BROWSER
		else
		{
			UpdateMode = config.UpdateMode ?? UpdateMode.UnlockedStep();
			inputProvider = new InputProviderBrowser(this);
			Input = inputProvider.Input;
			FileSystem = new(this);
			GraphicsDevice = new GraphicsDeviceWebGPU(this);
			GraphicsDevice.CreateDevice(config.Flags);
			Window = new Window(this, config.WindowTitle, config.Width, config.Height, config.Fullscreen, config.Resizable,
				config.Flags.Has(AppFlags.ScaleWindowToDisplay));
		}
#else
		else
		{
			// set SDL logging method
			if (OperatingSystem.IsIOS())
				SDL_SetLogOutputFunctionAot(&HandleLogFromSDLAot, nint.Zero);
			else
				SDL_SetLogOutputFunction(HandleLogFromSDL, IntPtr.Zero);

			SDL_SetMainReady();

			// by default allow controller presses while unfocused,
			// let game decide if it should handle them
			SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
			if (OperatingSystem.IsIOS())
				SDL_SetHint(SDL_HINT_TOUCH_MOUSE_EVENTS, "0");

			// initialize SDL3
			{
				var initFlags =
					SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_TIMER | SDL_InitFlags.SDL_INIT_EVENTS |
					SDL_InitFlags.SDL_INIT_JOYSTICK | SDL_InitFlags.SDL_INIT_GAMEPAD;

				if (!SDL_Init(initFlags))
					throw CreateExceptionFromSDL(nameof(SDL_Init));
			}

			// setup event watcher
			eventFilter = EventWatcher;
			if (!OperatingSystem.IsIOS())
				SDL_AddEventWatch(eventFilter, nint.Zero);

			// Create Modules
			UpdateMode = config.UpdateMode ?? UpdateMode.FixedStep(60);
			inputProvider = config.Flags.Has(AppFlags.Offscreen) ? new InputProviderHeadless() : new InputProviderSDL(this);
			Input = inputProvider.Input;
			FileSystem = new(this);
			GraphicsDevice = new GraphicsDeviceSDL(this, config.PreferredGraphicsDriver, config.FramesInFlight);
			GraphicsDevice.CreateDevice(config.Flags);
			Window = config.Flags.Has(AppFlags.Offscreen) ? null :
				new Window(this, config.WindowTitle, config.Width, config.Height, config.Fullscreen, config.Resizable,
					config.Flags.Has(AppFlags.ScaleWindowToDisplay));

			// try to load default SDL gamepad mappings
			Input.AddDefaultSDLGamepadMappings(AppContext.BaseDirectory);
		}
#endif
	}

	~App() => Dispose(false);

	/// <summary>
	/// Disposes the Application resources. May be called after <see cref="Run"/>,
	/// but not during any <see cref="App"/> callbacks (<see cref="Startup"/>, <see cref="Update"/>, <see cref="Render"/>, <see cref="Shutdown"/>)
	/// </summary>
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	private void Dispose(bool disposing)
	{
		if (!Disposed)
		{
			if (Running)
				throw new Exception("Cannot dispose App while running");

			if (disposing)
			{
				for (int i = windows.Count - 1; i >= 0; i --)
					windows[i].Destroy();
				DestroyWaitingWindows();

				GraphicsDevice.Shutdown();
				GraphicsDevice.DestroyDevice();
#if !BROWSER
				if (inputProvider is InputProviderSDL sdlInputProvider)
					sdlInputProvider.CloseDevices();
#endif
				mainThreadQueue.Clear();
			}

#if !BROWSER
			if (!config.Flags.Has(AppFlags.Headless))
				SDL_Quit();
#endif
			Disposed = true;
		}
	}

	/// <summary>
	/// Called when the Application has Started and is ready to be used
	/// </summary>
	protected abstract void Startup();

	/// <summary>
	/// Called as the Application shuts down. This should be used to finalize and dispose any resources.
	/// </summary>
	protected abstract void Shutdown();

	/// <summary>
	/// Called once per frame as the Application updates
	/// </summary>
	protected abstract void Update();

	/// <summary>
	/// Called when the Application renders to the Window
	/// </summary>
	protected abstract void Render();

	/// <summary>
	/// Runs the Application
	/// </summary>
	public void Run()
	{
		if (Disposed)
			throw DisposedException;
		if (Running)
			throw new Exception("Application is already running");
		if (Exiting)
			throw new Exception("Application is still exiting");

		Running = true;
		Time = new();
		lastUpdateTime = TimeSpan.Zero;
		fixedAccumulator = TimeSpan.Zero;
		timer.Restart();

#if BROWSER
		if (browserStarted)
			throw new Exception("Browser application is already started");

		browserStarted = true;
		browserRunningApp = this;
		foreach (var window in windows)
			window.Show();
		Startup();
		BrowserWebGPU.StartRenderLoop(BrowserFrame);
		return;
#else
		// wrap in a try/finally so if anything here throws, Dispose
		// won't then also throw due to the Application still "Running"
		try
		{
			// poll events once, so input has controller state before Startup
			PollEvents();
			inputProvider.Update(Time);
			foreach (var window in windows)
				window.Show();
			Startup();

			// begin normal game loop
			while (!Exiting)
				Tick();

			// make sure all queued main thread actions have been run
			while (mainThreadQueue.TryDequeue(out var action))
				action.Invoke();

			// shutdown
			Shutdown();
			foreach (var window in windows)
				window.Hide();
			if (inputProvider is InputProviderSDL sdlInputProvider)
				sdlInputProvider.CloseDevices();
		}
		finally
		{
			Running = false;
			Exiting = false;
		}
#endif
	}

#if BROWSER
	// Synchronous per-frame callback invoked by JS from inside requestAnimationFrame
	// (the ?syncTick path). Runs one frame and returns whether to keep looping.
	// Mirrors RunBrowserLoop's shutdown sequence on exit.
	private bool BrowserFrame()
	{
		try
		{
			if (!Exiting)
				Tick();
		}
		catch (Exception exception)
		{
			Log.Error(exception.ToString());
			throw;
		}

		if (!Exiting)
			return true;

		while (mainThreadQueue.TryDequeue(out var action))
			action.Invoke();
		Shutdown();
		foreach (var window in windows)
			window.Hide();
		Running = false;
		Exiting = false;
		if (ReferenceEquals(browserRunningApp, this))
			browserRunningApp = null;
		return false;
	}

	private async Task RunBrowserLoop()
	{
		try
		{
			while (!Exiting)
			{
				await BrowserWebGPU.WaitForAnimationFrame();
				Tick();
			}

			while (mainThreadQueue.TryDequeue(out var action))
				action.Invoke();

			Shutdown();
			foreach (var window in windows)
				window.Hide();
		}
		catch (Exception exception)
		{
			Log.Error(exception.ToString());
			throw;
		}
		finally
		{
			Running = false;
			Exiting = false;
			if (ReferenceEquals(browserRunningApp, this))
				browserRunningApp = null;
		}
	}
#endif

	/// <summary>
	/// Notifies the Application to Exit.
	/// The Application may finish the current frame before exiting.
	/// </summary>
	public void Exit()
	{
		if (Running)
			Exiting = true;
	}

	/// <summary>
	/// If the current thread is the Main thread the Application was Run on
	/// </summary>
	public bool IsMainThread()
		=> Environment.CurrentManagedThreadId == mainThreadID;

	/// <summary>
	/// Queues an action to be run on the Main Thread.
	/// If this is called from the main thread, it is invoked immediately.
	/// </summary>
	public void RunOnMainThread(Action action)
	{
		if (Running && IsMainThread())
			action();
		else
			mainThreadQueue.Enqueue(action);
	}

	/// <summary>
	/// Sets whether the Mouse Cursor should be visible while over any of the Windows
	/// </summary>
	public void SetMouseVisible(bool enabled)
	{
#if BROWSER
		BrowserWebGPU.SetMouseVisible(enabled);
#else
		if (enabled == SDL_CursorVisible())
			return;

		var result = enabled ? SDL_ShowCursor() : SDL_HideCursor();
		if (!result)
			Log.Warning($"Failed to set Mouse visibility: {SDL_GetError()}");
#endif
	}

	/// <summary>
	/// Sets the Mouse Cursor. If null, resets the Cursor to the default OS cursor.
	/// </summary>
	public void SetMouseCursor(Cursor? cursor)
	{
#if BROWSER
		currentCursor = cursor;
#else
		if (currentCursor == cursor)
			return;

		if (cursor == null)
		{
			currentCursor = null;
			SDL_SetCursor(SDL_GetDefaultCursor());
			return;
		}

		if (cursor.Disposed)
			throw new Exception("Using an invalid cursor!");

		if (SDL_SetCursor(cursor.Handle))
			currentCursor = cursor;
		else
			Log.Warning($"Failed to set Mouse Cursor: {SDL_GetError()}");
#endif
	}

	internal void WindowCreated(Window window)
	{
		windows.Add(window);
		GraphicsDevice.WindowCreated(window);
	}

	internal void WindowMarkedForDestruction(Window window)
	{
		if (!window.IsDestroyed)
		{
			if (!windowDestroyingQueue.Contains(window))
				windowDestroyingQueue.Enqueue(window);
			windows.Remove(window);
		}
	}

	internal void DestroyWaitingWindows()
	{
		while (windowDestroyingQueue.TryDequeue(out var window))
		{
			GraphicsDevice.WindowDestroyed(window);
#if !BROWSER
			SDL_DestroyWindow(window.Handle);
#endif
			window.Destroyed();
		}
	}

	private void Tick()
	{
		void Step(TimeSpan delta)
		{
			Time = Time.Advance(delta);

			// warp mouse to center of the window if Relative Mode is enabled
#if !BROWSER
			if (Window != null && SDL_GetWindowRelativeMouseMode(Window.Handle) && Window.Focused)
				SDL_WarpMouseInWindow(Window.Handle, Window.Width / 2, Window.Height / 2);
#endif

			PollEvents();
			inputProvider.Update(Time);
			FramePool.NextFrame();

			while (mainThreadQueue.TryDequeue(out var action))
				action.Invoke();

			Update();
		}

		if (GraphicsDevice.WaitBeforeUpdate)
			GraphicsDevice.WaitForSwapchains();

		var update = UpdateMode;
		var currentTime = timer.Elapsed;
		var deltaTime = currentTime - lastUpdateTime;
		lastUpdateTime = currentTime;

		// update in Fixed Mode
		if (update.Mode == UpdateMode.Modes.Fixed)
		{
			fixedAccumulator += deltaTime;

			// Do not let us run too fast
			if (update.FixedWaitEnabled)
			{
				while (fixedAccumulator < update.FixedTargetTime)
				{
					int milliseconds = (int)(update.FixedTargetTime - fixedAccumulator).TotalMilliseconds;
					Thread.Sleep(milliseconds);

					currentTime = timer.Elapsed;
					deltaTime = currentTime - lastUpdateTime;
					lastUpdateTime = currentTime;
					fixedAccumulator += deltaTime;
				}
			}

			// Do not allow any update to take longer than our maximum.
			if (fixedAccumulator > update.FixedMaxTime)
			{
				Time = Time.Advance(fixedAccumulator - update.FixedMaxTime);
				fixedAccumulator = update.FixedMaxTime;
			}

			// Do as many fixed updates as we can
			while (fixedAccumulator >= update.FixedTargetTime)
			{
				fixedAccumulator -= update.FixedTargetTime;
				Step(update.FixedTargetTime);
				if (Exiting)
					break;
			}
		}
		// update in Unthrottled Mode (back-to-back fixed steps, no wall-clock wait)
		else if (update.Mode == UpdateMode.Modes.Unthrottled)
		{
			// Headless: pure tight loop — no rendering
			if (config.Flags.Has(AppFlags.Headless))
			{
				while (!Exiting)
					Step(update.FixedTargetTime);
			}
			else
			{
			// With graphics (+ vsync): run a fixed number of simulation
			// steps per render frame, then present.  Default 60 steps
			// gives ~60× real-time at 60 FPS with vsync on.
			while (!Exiting)
			{
				int steps = UnthrottledStepsPerFrame;
				for (int i = 0; i < steps && !Exiting; i++)
					Step(update.FixedTargetTime);

				Time = Time.AdvanceRenderFrame();
				Render();
				var presentStart = Stopwatch.GetTimestamp();
				GraphicsDevice.Present();
				GraphicsDevice.LastPresentDuration = Stopwatch.GetElapsedTime(presentStart);
				DestroyWaitingWindows();
				}
			}
		}
		// update in Unlocked Mode
		else
		{
			Step(deltaTime);
		}

		// render
		if (!config.Flags.Has(AppFlags.Headless))
		{
			Time = Time.AdvanceRenderFrame();
			Render();
			var presentStart = Stopwatch.GetTimestamp();
			GraphicsDevice.Present();
			GraphicsDevice.LastPresentDuration = Stopwatch.GetElapsedTime(presentStart);
		}

		// destroy any queued windows
		if (!config.Flags.Has(AppFlags.Headless))
			DestroyWaitingWindows();
	}

#if !BROWSER
	private unsafe bool EventWatcher(nint userdata, SDL_Event* eventPtr)
	{
		var type = (SDL_EventType)eventPtr->type;

		if (type == SDL_EventType.SDL_EVENT_DID_ENTER_FOREGROUND)
			HandleAppLifecycleEvent(AppEvents.EnterForeground);
		else if (type == SDL_EventType.SDL_EVENT_WILL_ENTER_BACKGROUND)
			HandleAppLifecycleEvent(AppEvents.EnterBackground);

		return true;
	}

	private void ProcessEvent(SDL_Event ev)
	{
		switch ((SDL_EventType)ev.type)
		{
		case SDL_EventType.SDL_EVENT_DID_ENTER_FOREGROUND:
		case SDL_EventType.SDL_EVENT_WILL_ENTER_FOREGROUND:
		case SDL_EventType.SDL_EVENT_DID_ENTER_BACKGROUND:
		case SDL_EventType.SDL_EVENT_WILL_ENTER_BACKGROUND:
			if (OperatingSystem.IsIOS())
			{
				if ((SDL_EventType)ev.type == SDL_EventType.SDL_EVENT_DID_ENTER_FOREGROUND)
					HandleAppLifecycleEvent(AppEvents.EnterForeground);
				else if ((SDL_EventType)ev.type == SDL_EventType.SDL_EVENT_WILL_ENTER_BACKGROUND)
					HandleAppLifecycleEvent(AppEvents.EnterBackground);
			}
			break;

		case SDL_EventType.SDL_EVENT_QUIT:
			if (Running && !Exiting)
			{
				if (OnExitRequested != null)
					OnExitRequested();
				else
					Exit();
			}
			break;

		// input
		case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
		case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
		case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
		case SDL_EventType.SDL_EVENT_FINGER_DOWN:
		case SDL_EventType.SDL_EVENT_FINGER_UP:
		case SDL_EventType.SDL_EVENT_FINGER_MOTION:
		case SDL_EventType.SDL_EVENT_FINGER_CANCELED:
		case SDL_EventType.SDL_EVENT_PINCH_BEGIN:
		case SDL_EventType.SDL_EVENT_PINCH_UPDATE:
		case SDL_EventType.SDL_EVENT_PINCH_END:
		case SDL_EventType.SDL_EVENT_KEY_DOWN:
		case SDL_EventType.SDL_EVENT_KEY_UP:
		case SDL_EventType.SDL_EVENT_TEXT_INPUT:
		case SDL_EventType.SDL_EVENT_JOYSTICK_ADDED:
		case SDL_EventType.SDL_EVENT_JOYSTICK_REMOVED:
		case SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_DOWN:
		case SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_UP:
		case SDL_EventType.SDL_EVENT_JOYSTICK_AXIS_MOTION:
		case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
		case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
		case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
		case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
		case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
			if (inputProvider is InputProviderSDL sdlInputProvider)
				sdlInputProvider.OnEvent(ev);
			break;

		case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
		case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
		case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER:
		case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE:
		case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
		case SDL_EventType.SDL_EVENT_WINDOW_RESTORED:
		case SDL_EventType.SDL_EVENT_WINDOW_MAXIMIZED:
		case SDL_EventType.SDL_EVENT_WINDOW_MINIMIZED:
		case SDL_EventType.SDL_EVENT_WINDOW_ENTER_FULLSCREEN:
		case SDL_EventType.SDL_EVENT_WINDOW_LEAVE_FULLSCREEN:
		case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
			foreach (var window in windows)
				if (window.ID == ev.window.windowID)
				{
					window.OnEvent((SDL_EventType)ev.type switch
					{
						SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED => WindowEvent.FocusGained,
						SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST => WindowEvent.FocusLost,
						SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER => WindowEvent.MouseEntered,
						SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE => WindowEvent.MouseLeft,
						SDL_EventType.SDL_EVENT_WINDOW_RESIZED => WindowEvent.Resized,
						SDL_EventType.SDL_EVENT_WINDOW_RESTORED => WindowEvent.Restored,
						SDL_EventType.SDL_EVENT_WINDOW_MAXIMIZED => WindowEvent.Maximized,
						SDL_EventType.SDL_EVENT_WINDOW_MINIMIZED => WindowEvent.Minimized,
						SDL_EventType.SDL_EVENT_WINDOW_ENTER_FULLSCREEN => WindowEvent.FullscreenEntered,
						SDL_EventType.SDL_EVENT_WINDOW_LEAVE_FULLSCREEN => WindowEvent.FullscreenExited,
						_ => WindowEvent.CloseRequested,
					});
					break;
				}
			break;

		default:
			break;
		}
	}
#endif

	private void HandleAppLifecycleEvent(AppEvents type)
	{
		GraphicsDevice.OnAppBackgroundChanged(type == AppEvents.EnterBackground);
		OnEvent?.Invoke(type);
	}

	private void PollEvents()
	{
#if BROWSER
		return;
#else
		if (config.Flags.Has(AppFlags.Headless))
			return;

		// we shouldn't need to pump events here, but we've found that if we don't,
		// there are issues on MacOS with it not receiving mouse clicks correctly
		SDL_PumpEvents();

		while (SDL_PollEvent(out var ev) && ev.type != (uint)SDL_EventType.SDL_EVENT_POLL_SENTINEL)
			ProcessEvent(ev);
#endif
	}

	/// <summary>
	/// Creates an Exception with information from SDL_GetError()
	/// </summary>
	internal static Exception CreateExceptionFromSDL(string sdlMethod, string? fosterInfo = null)
		=> new(CreateErrorMessageFromSDL(sdlMethod, fosterInfo));

	/// <summary>
	/// Creates an error string with information from SDL_GetError()
	/// </summary>
	internal static string CreateErrorMessageFromSDL(string sdlMethod, string? fosterInfo = null)
#if BROWSER
		=> $"{(fosterInfo != null ? $"{fosterInfo}. " : "")}{sdlMethod} failed";
#else
		=> $"{(fosterInfo != null ? $"{fosterInfo}. " : "")}{sdlMethod} failed: {SDL_GetError()}";
#endif

#if !BROWSER
	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	internal static unsafe void HandleLogFromSDLAot(nint userdata, int category, SDL_LogPriority priority, byte* message)
		=> HandleLogFromSDL(userdata, category, priority, message);

	internal static unsafe void HandleLogFromSDL(IntPtr userdata, int category, SDL_LogPriority priority, byte* message)
	{
		switch (priority)
		{
			case SDL_LogPriority.SDL_LOG_PRIORITY_VERBOSE:
			case SDL_LogPriority.SDL_LOG_PRIORITY_DEBUG:
			case SDL_LogPriority.SDL_LOG_PRIORITY_INFO:
				Log.Info(new nint(message));
				break;
			case SDL_LogPriority.SDL_LOG_PRIORITY_WARN:
				Log.Warning(new nint(message));
				break;
			case SDL_LogPriority.SDL_LOG_PRIORITY_ERROR:
		case SDL_LogPriority.SDL_LOG_PRIORITY_CRITICAL:
			Log.Error(new nint(message));
			break;
		}
	}
#endif
}
