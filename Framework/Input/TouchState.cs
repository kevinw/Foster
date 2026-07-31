using System.Numerics;

namespace Foster.Framework;

/// <summary>
/// Stores direct touch state for the current frame.
/// </summary>
public sealed class TouchState
{
	public const int MaxTouches = 8;
	internal static readonly TouchState ClearedState = new();

	public readonly struct Finger
	{
		public readonly ulong Id;
		public readonly Vector2 Position;
		public readonly Vector2 Delta;
		public readonly float Pressure;
		public readonly bool Down;
		public readonly bool Pressed;
		public readonly bool Released;
		public readonly bool Canceled;

		internal Finger(ulong id, Vector2 position, Vector2 delta, float pressure, bool down, bool pressed, bool released, bool canceled)
		{
			Id = id;
			Position = position;
			Delta = delta;
			Pressure = pressure;
			Down = down;
			Pressed = pressed;
			Released = released;
			Canceled = canceled;
		}
	}

	public TimeSpan InputTimestamp { get; private set; }
	public int Count { get; private set; }
	public int DownCount { get; private set; }
	public float PinchScale { get; private set; } = 1.0f;
	public bool PinchPressed { get; private set; }
	public bool PinchDown { get; private set; }
	public bool PinchReleased { get; private set; }

	private readonly ulong[] ids = new ulong[MaxTouches];
	private readonly Vector2[] positions = new Vector2[MaxTouches];
	private readonly Vector2[] deltas = new Vector2[MaxTouches];
	private readonly float[] pressures = new float[MaxTouches];
	private readonly bool[] down = new bool[MaxTouches];
	private readonly bool[] pressed = new bool[MaxTouches];
	private readonly bool[] released = new bool[MaxTouches];
	private readonly bool[] canceled = new bool[MaxTouches];

	public Finger this[int index]
		=> new(ids[index], positions[index], deltas[index], pressures[index], down[index], pressed[index], released[index], canceled[index]);

	public Vector2 Center
	{
		get
		{
			var center = Vector2.Zero;
			if (DownCount <= 0)
				return center;

			for (var i = 0; i < Count; i++)
			{
				if (!down[i])
					continue;
				center += positions[i];
			}
			return center / DownCount;
		}
	}

	public Vector2 CenterDelta
	{
		get
		{
			var delta = Vector2.Zero;
			if (DownCount <= 0)
				return delta;

			for (var i = 0; i < Count; i++)
			{
				if (!down[i])
					continue;
				delta += deltas[i];
			}
			return delta / DownCount;
		}
	}

	public TouchState Snapshot()
	{
		var result = new TouchState();
		result.Copy(this);
		return result;
	}

	public void Snapshot(TouchState into)
		=> into.Copy(this);

	public void Clear()
		=> ClearedState.Copy(this);

	internal void Copy(TouchState other)
	{
		Count = other.Count;
		DownCount = other.DownCount;
		PinchScale = other.PinchScale;
		PinchPressed = other.PinchPressed;
		PinchDown = other.PinchDown;
		PinchReleased = other.PinchReleased;
		InputTimestamp = other.InputTimestamp;
		Array.Copy(other.ids, ids, MaxTouches);
		Array.Copy(other.positions, positions, MaxTouches);
		Array.Copy(other.deltas, deltas, MaxTouches);
		Array.Copy(other.pressures, pressures, MaxTouches);
		Array.Copy(other.down, down, MaxTouches);
		Array.Copy(other.pressed, pressed, MaxTouches);
		Array.Copy(other.released, released, MaxTouches);
		Array.Copy(other.canceled, canceled, MaxTouches);
	}

	internal void Step(in Time time)
	{
		PinchScale = 1.0f;
		PinchPressed = false;
		PinchReleased = false;

		for (var i = Count - 1; i >= 0; i--)
		{
			deltas[i] = Vector2.Zero;
			pressed[i] = false;
			canceled[i] = false;
			if (released[i])
			{
				RemoveAt(i);
				continue;
			}
			released[i] = false;
		}
	}

	internal void OnFinger(ulong id, Vector2 position, Vector2 delta, float pressure, bool fingerDown, bool fingerUp, bool fingerCanceled, in TimeSpan time)
	{
		var index = IndexOf(id);
		if (index < 0)
		{
			if (!fingerDown || Count >= MaxTouches)
				return;
			index = Count++;
			ids[index] = id;
		}

		positions[index] = position;
		deltas[index] = delta;
		pressures[index] = pressure;
		InputTimestamp = time;

		if (fingerDown)
		{
			down[index] = true;
			pressed[index] = true;
			released[index] = false;
			canceled[index] = false;
		}
		else if (fingerUp || fingerCanceled)
		{
			down[index] = false;
			released[index] = true;
			canceled[index] = fingerCanceled;
		}

		RefreshDownCount();
	}

	internal void OnPinch(float scale, bool begin, bool update, bool end, in TimeSpan time)
	{
		if (begin)
		{
			PinchPressed = true;
			PinchDown = true;
		}
		else if (end)
		{
			PinchReleased = true;
			PinchDown = false;
		}
		else if (update)
		{
			PinchDown = true;
		}

		PinchScale = scale;
		InputTimestamp = time;
	}

	private int IndexOf(ulong id)
	{
		for (var i = 0; i < Count; i++)
			if (ids[i] == id)
				return i;
		return -1;
	}

	private void RemoveAt(int index)
	{
		var last = Count - 1;
		if (index != last)
		{
			ids[index] = ids[last];
			positions[index] = positions[last];
			deltas[index] = deltas[last];
			pressures[index] = pressures[last];
			down[index] = down[last];
			pressed[index] = pressed[last];
			released[index] = released[last];
			canceled[index] = canceled[last];
		}

		ids[last] = 0;
		positions[last] = Vector2.Zero;
		deltas[last] = Vector2.Zero;
		pressures[last] = 0;
		down[last] = false;
		pressed[last] = false;
		released[last] = false;
		canceled[last] = false;
		Count--;
		RefreshDownCount();
	}

	private void RefreshDownCount()
	{
		DownCount = 0;
		for (var i = 0; i < Count; i++)
			if (down[i])
				DownCount++;
	}
}
