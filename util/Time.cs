
using SDL3;

namespace panpan.Util
{
    public class Time
    {
        private static double deltaTime;
        private static ulong lastFrameTime;

        public static float DeltaTime => (float)deltaTime;
        public static float Elapsed()
        {
            float seconds = SDL.GetTicks() / 1000.0f;
            return seconds;
        }

        public static void Update()
        {
            ulong now = SDL.GetPerformanceCounter();
            deltaTime = (double)((now-lastFrameTime)*1000 / (double)SDL.GetPerformanceFrequency())/1000.0;//Time.Elapsed() - lastFrameTime;
            lastFrameTime = now;//Time.Elapsed();
            if(deltaTime > 1.0)
                deltaTime = 1.0;
        }
    }

    public delegate void TimerCallback();

    public class PTimer
    {
        private static readonly List<PTimer> timers = new List<PTimer>();
        private static readonly List<PTimer> tickList = new List<PTimer>();

        private TimerCallback? timerCompleteCallback;
        private bool running;
        private bool complete;
        private float duration;
        private float elapsed;
        private bool repeat;

        public float Duration => duration;
        public float Elapsed => elapsed;
        public float Remaining => MathF.Max(duration - elapsed, 0.0f);
        public bool Running => running;
        public bool Complete => complete;
        public bool Repeat { get => repeat; set => repeat = value; }

        public float Progress => duration <= 0.0f ? 1.0f : Math.Clamp(elapsed / duration, 0.0f, 1.0f);

        public PTimer(float durationSeconds, TimerCallback? timerCompleteCallback, bool repeat = false)
        {
            this.duration = durationSeconds;
            this.timerCompleteCallback = timerCompleteCallback;
            this.repeat = repeat;
            running = false;
            complete = false;
        }

        public static void UpdateAll(float deltaTime)
        {
            if (timers.Count == 0)
                return;

            tickList.Clear();
            tickList.AddRange(timers);

            foreach (var timer in tickList)
            {
                timer.Tick(deltaTime);
            }
        }

        public static void StopAll()
        {
            foreach (var timer in timers)
            {
                timer.running = false;
            }
            timers.Clear();
        }

        private void Tick(float deltaTime)
        {
            if (!running)
                return;

            elapsed += deltaTime;
            if (elapsed < duration)
                return;

            if (repeat)
            {
                elapsed -= duration;
                if (elapsed >= duration)
                    elapsed = 0.0f;
            }
            else
            {
                Stop();
                complete = true;
            }

            timerCompleteCallback?.Invoke();
        }

        public void Start()
        {
            Restart();
        }

        public void Restart()
        {
            elapsed = 0.0f;
            complete = false;

            if (!running)
            {
                running = true;
                timers.Add(this);
            }
        }

        public void Stop()
        {
            if (!running)
                return;

            running = false;
            timers.Remove(this);
        }
    }
}
