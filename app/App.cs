using SDL3;
using System.Runtime.InteropServices;

using panpan.Rendering;
using panpan.Assets;
using panpan.Scene;
using GlmSharp;
using panpan.Collision;
using panpan.Util;
using FreeTypeSharp;
using static FreeTypeSharp.FT;
using static FreeTypeSharp.FT_LOAD;
using static FreeTypeSharp.FT_Render_Mode_;
using panpan.Rendering.Util;

namespace panpan
{
    public enum Platform
    {
        Mac,
        Windows,
        Linux,
        Nintendo,
        Playstation,
        UWP,
        Unkown
    }
    public class App
    {

        // Rendering data
        private static Platform platform;
        private static ivec2 gameSize;
        static float fps;
        static float fpsUpdateTime;
        static int fpsFrameCount;
        const float FPS_UPDATE_INTERVAL = 1.0f; // Update FPS every 1 second
        static nint gpuDevice;
        static SDL.GPUShaderFormat gpuShaderFormat;
        static nint window;
        static nint commandBuffer;
        static nint renderPass;
        static nint swapchainTexture;
        public static bool isFullScreen = false;
        public static bool isScreenSizeDirty = false;
        static readonly List<nint> renderFences = new List<nint>();
        static ImGuiController? imguiController;

        // Freetype

        static public unsafe FT_LibraryRec_* FreetypeLib;

        // Managers
        static SceneManager sceneManager = null!;
        static CollisionManager collisionManager = null!;
        static Input inputManager = null!;

        // Scene
        private readonly Scene.Scene startScene;

        // Settings
        static vec4 bgColor;

        // Backbuffer
        static RenderTarget backBuffer = null!;

        public static bool IsFullScreen => isFullScreen;

        private static vec2 cachedScreenBounds;

        private const string LOG_TAG = "panpan-App";

        public App(string title, Scene.Scene startScene, int width = 320, int height = 180)
        {
            // General
            bgColor = Color.SkyBlue;
            gameSize = new ivec2(width, height);
            this.startScene = startScene;

            // Freetype
            unsafe
            {
                FT_LibraryRec_* lib = null;
                FT_Init_FreeType(&lib);
                FreetypeLib = lib;
            }
            // SDL GPU

            SDL.Init(SDL.InitFlags.Video);

            gpuShaderFormat = SDL.GPUShaderFormat.SPIRV;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                platform = Platform.Mac;
                gpuShaderFormat = SDL.GPUShaderFormat.MSL;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                platform = Platform.Windows;
                // gpuShaderFormat |= SDL.GPUShaderFormat.DXBC | SDL.GPUShaderFormat.DXIL;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                platform = Platform.Linux;
            }
            else
            {
                platform = Platform.Unkown;
            }

            gpuDevice = SDL.CreateGPUDevice(gpuShaderFormat, true, null);
            if (gpuDevice == nint.Zero)
            {
                Log.Error($"Error: Failed to create gpu device: {SDL.GetError()}");
                Environment.Exit(1);
            }
            window = SDL.CreateWindow(title, width, height, 0);

            if (!SDL.ClaimWindowForGPUDevice(gpuDevice, window))
            {
                Log.Error($"Error: Failed to create window for gpu device: {SDL.GetError()}");
                Environment.Exit(1);
            }

            Log.Info("App Info:", LOG_TAG);
            Log.Info($"     Platform Name: {platform}", LOG_TAG);
            Log.Info($"     GPU Shader Format: {gpuShaderFormat}", LOG_TAG);
            Log.Info($"     Window created {width}w  {height}h");

            SDL.SetGPUAllowedFramesInFlight(gpuDevice, 2);

            //

            AudioSystem.Init();
        }

        public static float GetFPS()
        {
            return fps;
        }

        public static int GetDrawCallCount()
        {
            return MeshRenderer.GetDrawCallCount();
        }

        public static nint GetDevice()
        {
            return gpuDevice;
        }

        public static nint GetWindow()
        {
            return window;
        }

        public static vec2 GetWindowSize()
        {
            return new vec2(backBuffer.Width, backBuffer.Height);
        }

        public static vec2 GetGameSize()
        {
            return gameSize;
        }

        public static Rect GetDisplayBounds()
        {
            SDL.Rect r;
            SDL.GetDisplayBounds(SDL.GetDisplayForWindow(window), out r);
            return new Rect(r.X,r.Y,r.W,r.H);
        }
        public static SDL.GPUShaderFormat GetShaderFormat()
        {
            return gpuShaderFormat;
        }

        public static Platform GetPlatform()
        {
            return platform;
        }

        public static nint GetCommandBuffer()
        {
            return commandBuffer;
        }
        public static SceneManager GetSceneManager()
        {
            return sceneManager;
        }
        public static CollisionManager GetCollisionManager()
        {
            return collisionManager;
        }

        public static nint GetRenderPass()
        {
            return renderPass;
        }

        public static void SetBGColor(vec4 col)
        {
            bgColor = col;
            backBuffer?.SetClearColor(bgColor);
        }

        public static void ToggleFullscreen()
        {
            SDL.SetWindowFullscreen(window, !isFullScreen);
            isFullScreen = !isFullScreen;

            isScreenSizeDirty = true;

            sceneManager.ActiveScene.OnFSToggle();
            if(isFullScreen)
                backBuffer.Resize((uint)GetDisplayBounds().Width, (uint)GetDisplayBounds().Height);
            else
                backBuffer.Resize((uint)gameSize.x, (uint)gameSize.y);

        }

        /// <summary>
        /// Initialize managers.
        /// </summary>
        /// <returns></returns>
        internal bool Init()
        {
            backBuffer = new RenderTarget((uint)gameSize.x, (uint)gameSize.y, bgColor);
            collisionManager = new CollisionManager(CollisionManager.ManagerType.SPACIAL_HASH, 8, new vec2(320, 180));
            sceneManager = new SceneManager();
            inputManager = new Input();
            imguiController = new ImGuiController();
            imguiController.Initialize(gpuDevice, window);
            
            DefaultMaterials.Init();
            sceneManager.SwapScene(startScene);

            Shapes.Init();
            return true;
        }

        /// <summary>
        /// Called by Project.cs to start the app.
        /// </summary>
        public void Run()
        {
            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            Init();
            float currentTime = Time.Elapsed();
            fpsUpdateTime = currentTime;
            fpsFrameCount = 0;

            float fixedUpdateAccumulator = 0.0f;

            while (!token.IsCancellationRequested)
            {
                MeshRenderer.ResetDrawCallCount();
                isScreenSizeDirty = false;

                Time.Update();
                currentTime = Time.Elapsed();
                while (SDL.PollEvent(out var evt))
                {
                    if (evt.Type == (uint)SDL.EventType.WindowCloseRequested)
                    {
                        tokenSource.Cancel();
                    }
                    imguiController?.ProcessEvent(ref evt);
                    Input.HandleEvents(evt);
                }
                Input.Update();
                AudioSystem.Update();
                imguiController?.NewFrame();

                float deltaTime = Time.DeltaTime;

                if(MathF.Abs(deltaTime - 1.0f/120.0f) < .0002f)
                {
                    deltaTime = 1.0f/120.0f;
                }
                if(MathF.Abs(deltaTime - 1.0f/60.0f) < .0002f)
                {
                    deltaTime = 1.0f/60.0f;
                }
                if(MathF.Abs(deltaTime - 1.0f/30.0f) < .0002f)
                {
                    deltaTime = 1.0f/30.0f;
                }

                fixedUpdateAccumulator += deltaTime;
                while(fixedUpdateAccumulator >= 1.0f / 62.0f)
                {
                    FixedUpdate();
                    fixedUpdateAccumulator -= 1.0f / 60.0f;
                }

                PTimer.UpdateAll(deltaTime);

                Update();
                Render();

                // Update FPS counter using time-based averaging
                fpsFrameCount++;
                float elapsedSinceUpdate = currentTime - fpsUpdateTime;
                if (elapsedSinceUpdate >= FPS_UPDATE_INTERVAL)
                {
                    fps = fpsFrameCount / elapsedSinceUpdate;
                    fpsFrameCount = 0;
                    fpsUpdateTime = currentTime;
                }
            }

            // cleanup

            unsafe
            {
                FT.FT_Done_FreeType(FreetypeLib);
            }

            imguiController?.Dispose();
            SDL.DestroyGPUDevice(gpuDevice);
            SDL.DestroyWindow(window);

        }

        /// <summary>
        /// Updates the active scene.
        /// </summary>
        private void Update()
        {
            sceneManager.ActiveScene.Update();
        }
        /// <summary>
        /// Updates the active scene at a fixed framerate.
        /// </summary>
        private void FixedUpdate()
        {
            sceneManager.ActiveScene.FixedUpdate();
        }

        /// <summary>
        /// Renders the active scene.
        /// </summary>
        private void Render()
        {
            
            backBuffer.SetDoesClear(true);
            SetRenderTarget(backBuffer);
            sceneManager.ActiveScene.Render();
            EndRenderPass();

            imguiController?.RenderUI();

            CreateDefaultRenderTarget(SDL.GPULoadOp.Clear);
            if (swapchainTexture != nint.Zero)
            {
                Draw.RenderTarget(backBuffer, new vec2(-gameSize.x/2, gameSize.y/2));
                imguiController?.RenderDrawData(commandBuffer, renderPass);
            }
            EndRenderPass();
            swapchainTexture = nint.Zero;
        }

        private static void CreateDefaultRenderTarget(SDL.GPULoadOp loadOp = SDL.GPULoadOp.Load)
        {
            WaitAndClearFences();
            commandBuffer = SDL.AcquireGPUCommandBuffer(gpuDevice);

            if (swapchainTexture == nint.Zero)
            {
                if (!SDL.WaitAndAcquireGPUSwapchainTexture(
                    commandBuffer, window,
                    out swapchainTexture,
                    out var _,
                    out var _
                ))
                {
                    Console.WriteLine("SDL Error WaitAndAcquireGPUSwapchainTexture: " + SDL.GetError());
                }
            }

            imguiController?.PrepareDrawData(commandBuffer);

            var colorTargetInfo = new SDL.GPUColorTargetInfo
            {
                Texture = swapchainTexture,
                LoadOp = loadOp,
                StoreOp = SDL.GPUStoreOp.Store
            };
            if (loadOp == SDL.GPULoadOp.Clear)
            {
                colorTargetInfo.ClearColor = new SDL.FColor
                {
                    R = bgColor.r,
                    G = bgColor.g,
                    B = bgColor.b,
                    A = 1.0f
                };
            }

            var ptr = SDL.StructureToPointer<SDL.GPUColorTargetInfo>(colorTargetInfo);
            renderPass = SDL.BeginGPURenderPass(
                commandBuffer,
                ptr,
                1,
                nint.Zero
            );

            Marshal.FreeHGlobal(ptr);
        }

        /// <summary>
        /// Resets to use the swapchain render target on a new render pass
        /// </summary>
        /// <param name="loadOp"></param>
        public static void ResetRenderTarget(bool clear = false)
        {
            EndRenderPass();
            backBuffer.SetDoesClear(clear);
            SetRenderTarget(backBuffer);
        }

        /// <summary>
        /// Sets the active render pass to use passed in render target
        /// </summary>
        /// <param name="target">Render target</param>
        public static void SetRenderTarget(RenderTarget target)
        {
            WaitAndClearFences();
            commandBuffer = SDL.AcquireGPUCommandBuffer(gpuDevice);
            renderPass = target.CreateRenderPass();
        }

        /// <summary>
        /// Ends currently active render pass.
        /// </summary>
        public static void EndRenderPass()
        {
            SDL.EndGPURenderPass(renderPass);
            SDL.SubmitGPUCommandBuffer(commandBuffer);
            //nint fence = SDL.SubmitGPUCommandBufferAndAcquireFence(commandBuffer);
            //renderFences.Add(fence);
        }

        private static void WaitAndClearFences() // Probably not needed?
        {
            if (renderFences.Count == 0)
            {
                return;
            }

            Log.Info("Clearing render fences...");

            SDL.WaitForGPUFences(GetDevice(), true, renderFences.ToArray(), (uint)renderFences.Count);

            foreach (var fence in renderFences)
            {
                SDL.ReleaseGPUFence(GetDevice(), fence);
            }
            renderFences.Clear();
        }
    }
}