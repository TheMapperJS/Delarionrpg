using System;
using System.IO;
using System.Numerics;
using System.Threading;
using Raylib_cs;
using UltralightNet;
using UltralightNet.AppCore;

namespace RaylibUltralightApp
{
    public enum AppState
    {
        MainMenu,
        Playing
    }

    class Program
    {
        private static AppState currentState = AppState.MainMenu;
        private static bool shouldExit = false;
        private static AppSettings settings = new AppSettings();
        private static Texture2D uiTexture;

        static unsafe void Main(string[] args)
        {
            bool screenshotArg = Array.Exists(args, arg => arg.Equals("--screenshot", StringComparison.OrdinalIgnoreCase) || arg.Equals("-s", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine("Initializing Raylib + Ultralight.NET Voxel World Engine...");

            // Load settings
            settings = AppSettings.Load();

            int width = settings.Width;
            int height = settings.Height;

            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
            Raylib.InitWindow(width, height, "CYBER QUEST - Greedy Meshed Voxel World Engine");
            Raylib.SetTargetFPS(settings.FpsCap);

            if (settings.Fullscreen && !Raylib.IsWindowFullscreen())
            {
                Raylib.ToggleFullscreen();
            }

            // Initialize 3D Camera
            Camera3D camera = new Camera3D();
            camera.Position = new Vector3(32.0f, 22.0f, 32.0f);
            camera.Target = new Vector3(33.0f, 21.8f, 35.0f);
            camera.Up = new Vector3(0.0f, 1.0f, 0.0f);
            camera.FovY = 65.0f;
            camera.Projection = CameraProjection.Perspective;

            // Initialize Voxel World
            World world = new World
            {
                RenderDistance = 4,
                MaxChunksPerFrame = 4
            };

            // Pre-populate initial chunks around starting position
            for (int i = 0; i < 25; i++)
            {
                world.Update(camera.Position);
            }

            // Initialize UltralightNet UI
            AppCoreMethods.SetPlatformFontLoader();
            var ulConfig = new ULConfig
            {
                ResourcePathPrefix = "resources/"
            };

            using var renderer = ULPlatform.CreateRenderer(ulConfig);
            var viewConfig = new ULViewConfig
            {
                IsTransparent = true,
                EnableImages = true,
                EnableJavaScript = true
            };

            using var view = renderer.CreateView((uint)width, (uint)height, viewConfig);

            // Set up JavaScript to C# message handler
            view.OnAddConsoleMessage += (source, level, message, lineNumber, columnNumber, sourceId) =>
            {
                Console.WriteLine($"[Ultralight Console] {message}");
                if (message.StartsWith("action:"))
                {
                    string action = message.Substring("action:".Length);
                    HandleUIAction(action, view);
                }
            };

            bool isPageLoaded = false;
            view.OnFinishLoading += (frameId, isMainFrame, url) =>
            {
                isPageLoaded = true;
                Console.WriteLine("Ultralight page loading finished.");
                SyncSettingsToUI(view);
            };

            // Load initial state HTML (assets/hud.html for Playing, assets/menu.html for MainMenu)
            if (screenshotArg)
            {
                SetAppState(AppState.Playing, view);
            }
            else
            {
                SetAppState(AppState.MainMenu, view);
            }

            // Wait brief moment for initial DOM ready
            int loadWaitCounter = 0;
            while (!isPageLoaded && loadWaitCounter < 100)
            {
                renderer.Update();
                Thread.Sleep(10);
                loadWaitCounter++;
            }

            // Create Raylib texture for UI rendering
            uiTexture = CreateUITexture(width, height);

            // Particle system variables for menu background
            int particleCount = 60;
            float[] particleX = new float[particleCount];
            float[] particleY = new float[particleCount];
            float[] particleSpeed = new float[particleCount];
            Random rand = new Random();

            for (int i = 0; i < particleCount; i++)
            {
                particleX[i] = rand.Next(0, width);
                particleY[i] = rand.Next(0, height);
                particleSpeed[i] = (float)(rand.NextDouble() * 1.5 + 0.5);
            }

            int frameCount = 0;

            // Main Application Loop
            while (!Raylib.WindowShouldClose() && !shouldExit)
            {
                frameCount++;

                // Detect dynamic window resize
                if (Raylib.IsWindowResized() && !Raylib.IsWindowMinimized())
                {
                    int currentW = Raylib.GetScreenWidth();
                    int currentH = Raylib.GetScreenHeight();
                    if (currentW > 0 && currentH > 0 && (currentW != width || currentH != height))
                    {
                        width = currentW;
                        height = currentH;
                        settings.Width = width;
                        settings.Height = height;
                        settings.Save();

                        OnResize(width, height, view);
                    }
                }

                // 1. Process UI Mouse Inputs
                int mouseX = Raylib.GetMouseX();
                int mouseY = Raylib.GetMouseY();

                view.FireMouseEvent(new ULMouseEvent
                {
                    Type = ULMouseEventType.MouseMoved,
                    X = mouseX,
                    Y = mouseY,
                    Button = ULMouseEventButton.None
                });

                if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    view.FireMouseEvent(new ULMouseEvent { Type = ULMouseEventType.MouseDown, X = mouseX, Y = mouseY, Button = ULMouseEventButton.Left });
                }
                if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                {
                    view.FireMouseEvent(new ULMouseEvent { Type = ULMouseEventType.MouseUp, X = mouseX, Y = mouseY, Button = ULMouseEventButton.Left });
                }

                // Global Hotkeys
                if (Raylib.IsKeyPressed(KeyboardKey.F12) || Raylib.IsKeyPressed(KeyboardKey.P))
                {
                    SaveScreenshot("screenshot_manual.png");
                }
                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    if (currentState == AppState.Playing)
                    {
                        SetAppState(AppState.MainMenu, view);
                    }
                    else
                    {
                        SetAppState(AppState.Playing, view);
                    }
                }

                // 2. Gameplay Updates
                if (currentState == AppState.Playing)
                {
                    // Free camera movement controls
                    float dt = Raylib.GetFrameTime();
                    float moveSpeed = 15.0f * dt;
                    if (Raylib.IsKeyDown(KeyboardKey.LeftShift)) moveSpeed *= 2.0f;

                    Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
                    Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));

                    if (Raylib.IsKeyDown(KeyboardKey.W))
                    {
                        camera.Position += forward * moveSpeed;
                        camera.Target += forward * moveSpeed;
                    }
                    if (Raylib.IsKeyDown(KeyboardKey.S))
                    {
                        camera.Position -= forward * moveSpeed;
                        camera.Target -= forward * moveSpeed;
                    }
                    if (Raylib.IsKeyDown(KeyboardKey.D))
                    {
                        camera.Position += right * moveSpeed;
                        camera.Target += right * moveSpeed;
                    }
                    if (Raylib.IsKeyDown(KeyboardKey.A))
                    {
                        camera.Position -= right * moveSpeed;
                        camera.Target -= right * moveSpeed;
                    }
                    if (Raylib.IsKeyDown(KeyboardKey.Space))
                    {
                        camera.Position += camera.Up * moveSpeed;
                        camera.Target += camera.Up * moveSpeed;
                    }
                    if (Raylib.IsKeyDown(KeyboardKey.LeftControl))
                    {
                        camera.Position -= camera.Up * moveSpeed;
                        camera.Target -= camera.Up * moveSpeed;
                    }

                    // Slow orbit rotation if idle/screenshot mode to show world dynamics
                    if (screenshotArg)
                    {
                        float angle = frameCount * 0.015f;
                        float dist = 30.0f;
                        camera.Position = new Vector3(32.0f + (float)Math.Cos(angle) * dist, 24.0f + (float)Math.Sin(angle * 0.5f) * 4.0f, 32.0f + (float)Math.Sin(angle) * dist);
                        camera.Target = new Vector3(32.0f, 15.0f, 32.0f);
                    }

                    // Update Chunk Loader around camera position
                    world.Update(camera.Position);
                }

                // 3. Update Ultralight View
                renderer.Update();
                renderer.Render();

                // 4. Sync Ultralight Surface to Raylib Texture
                ULSurface? surface = view.Surface;
                if (surface.HasValue)
                {
                    ULBitmap bitmap = surface.Value.Bitmap;
                    bitmap.SwapRedBlueChannels();
                    byte* pixels = bitmap.LockPixels();
                    if (pixels != null)
                    {
                        Raylib.UpdateTexture(uiTexture, pixels);
                        bitmap.UnlockPixels();
                    }
                    bitmap.SwapRedBlueChannels();
                }

                // 5. Draw Frame
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(135, 206, 235, 255)); // Sky Blue

                if (currentState == AppState.Playing)
                {
                    // Draw 3D Voxel World
                    Raylib.BeginMode3D(camera);
                    world.Draw();
                    Raylib.DrawGrid(20, 10.0f);
                    Raylib.EndMode3D();

                    // Draw RPG HUD Overlay (assets/hud.html UI)
                    Raylib.DrawTexture(uiTexture, 0, 0, Color.White);

                    // Draw Voxel Engine Stats
                    DrawHUD(world, camera);
                }
                else
                {
                    // Main Menu Background Particles & HTML Main Menu UI (assets/menu.html UI)
                    Raylib.ClearBackground(new Color(15, 20, 32, 255));
                    DrawBackground(width, height, particleX, particleY, particleCount, frameCount);
                    Raylib.DrawTexture(uiTexture, 0, 0, Color.White);
                }

                Raylib.EndDrawing();

                // Handle CLI --screenshot flag
                if (screenshotArg && frameCount >= 20)
                {
                    Console.WriteLine("[CLI] Taking screenshot requested via --screenshot command...");
                    SaveScreenshot("screenshot.png");
                    shouldExit = true;
                }
            }

            // Cleanup
            world.Cleanup();
            Raylib.UnloadTexture(uiTexture);
            Raylib.CloseWindow();
            Console.WriteLine("Application exited cleanly.");
        }

        private static Texture2D CreateUITexture(int width, int height)
        {
            Image uiImage = Raylib.GenImageColor(width, height, Color.Blank);
            Raylib.ImageFormat(ref uiImage, PixelFormat.UncompressedR8G8B8A8);
            Texture2D tex = Raylib.LoadTextureFromImage(uiImage);
            Raylib.UnloadImage(uiImage);
            return tex;
        }

        private static void OnResize(int newWidth, int newHeight, View view)
        {
            Console.WriteLine($"[Window] Resized window to {newWidth}x{newHeight}");
            view.Resize((uint)newWidth, (uint)newHeight);
            Raylib.UnloadTexture(uiTexture);
            uiTexture = CreateUITexture(newWidth, newHeight);
            SyncSettingsToUI(view);
        }

        private static void SyncSettingsToUI(View view)
        {
            string js = $"if (typeof setSettingsUI === 'function') {{ setSettingsUI({settings.Width}, {settings.Height}, {settings.Fullscreen.ToString().ToLower()}, {settings.FpsCap}); }}";
            view.EvaluateScript(js, out _);
        }

        private static void SetAppState(AppState newState, View view)
        {
            currentState = newState;
            if (currentState == AppState.MainMenu)
            {
                string menuHtmlPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "menu.html");
                if (File.Exists(menuHtmlPath))
                {
                    view.HTML = File.ReadAllText(menuHtmlPath);
                }
                else
                {
                    view.HTML = "<html><body style=\"color:white;background:rgba(0,0,0,0.8);\"><h1>VOXEL QUEST</h1><button onclick=\"console.log('action:start')\">Start</button></body></html>";
                }
            }
            else if (currentState == AppState.Playing)
            {
                string hudHtmlPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "hud.html");
                if (File.Exists(hudHtmlPath))
                {
                    view.HTML = File.ReadAllText(hudHtmlPath);
                }
            }
        }

        private static void HandleUIAction(string action, View view)
        {
            if (action == "start")
            {
                Console.WriteLine("[RPG UI] 'Enter Voxel Realm' clicked! Switching state to Playing.");
                SetAppState(AppState.Playing, view);
            }
            else if (action == "exit")
            {
                if (currentState == AppState.Playing)
                {
                    Console.WriteLine("[RPG UI] 'Main Menu' clicked! Returning to Main Menu.");
                    SetAppState(AppState.MainMenu, view);
                }
                else
                {
                    Console.WriteLine("[RPG UI] 'Abandon Realm' clicked! Closing application.");
                    shouldExit = true;
                }
            }
            else if (action.StartsWith("resolution:"))
            {
                string resStr = action.Substring("resolution:".Length);
                string[] parts = resStr.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                {
                    Console.WriteLine($"[Settings] Resolution changed to {w}x{h}");
                    settings.Width = w;
                    settings.Height = h;
                    settings.Save();

                    Raylib.SetWindowSize(w, h);
                    OnResize(w, h, view);
                }
            }
            else if (action.StartsWith("fullscreen:"))
            {
                string fsStr = action.Substring("fullscreen:".Length);
                if (bool.TryParse(fsStr, out bool fs))
                {
                    Console.WriteLine($"[Settings] Fullscreen set to {fs}");
                    settings.Fullscreen = fs;
                    settings.Save();

                    bool isCurrentlyFS = Raylib.IsWindowFullscreen();
                    if (fs != isCurrentlyFS)
                    {
                        Raylib.ToggleFullscreen();
                        int w = Raylib.GetScreenWidth();
                        int h = Raylib.GetScreenHeight();
                        OnResize(w, h, view);
                    }
                }
            }
            else if (action.StartsWith("fpsCap:"))
            {
                string fpsStr = action.Substring("fpsCap:".Length);
                if (int.TryParse(fpsStr, out int fps))
                {
                    Console.WriteLine($"[Settings] FPS cap changed to {fps}");
                    settings.FpsCap = fps;
                    settings.Save();
                    Raylib.SetTargetFPS(fps);
                }
            }
            else if (action.StartsWith("volume:"))
            {
                string volStr = action.Substring("volume:".Length);
                Console.WriteLine($"[RPG UI] Master volume set to: {volStr}%");
            }
            else if (action.StartsWith("equipItem:"))
            {
                string item = action.Substring("equipItem:".Length);
                Console.WriteLine($"[RPG UI] Relic equipped/inspected: {item}");
            }
            else if (action.StartsWith("quests:"))
            {
                string quest = action.Substring("quests:".Length);
                Console.WriteLine($"[RPG UI] Quest selected in codex: {quest}");
            }
            else if (action.StartsWith("openModal:"))
            {
                string modalId = action.Substring("openModal:".Length);
                Console.WriteLine($"[RPG UI] Opened RPG panel modal: {modalId}");
                SyncSettingsToUI(view);
            }
            else if (action.StartsWith("closeModal:"))
            {
                string modalId = action.Substring("closeModal:".Length);
                Console.WriteLine($"[RPG UI] Closed RPG panel modal: {modalId}");
            }
            else
            {
                Console.WriteLine($"[RPG UI] Action received: {action}");
            }
        }

        private static void DrawHUD(World world, Camera3D camera)
        {
            int panelX = 10;
            int panelY = 10;
            int panelW = 340;
            int panelH = 190;

            Raylib.DrawRectangle(panelX, panelY, panelW, panelH, new Color(0, 0, 0, 180));
            Raylib.DrawRectangleLines(panelX, panelY, panelW, panelH, new Color(0, 220, 255, 200));

            Raylib.DrawText("VOXEL ENGINE - GREEDY MESHING", panelX + 12, panelY + 10, 16, Color.Gold);
            Raylib.DrawText($"FPS: {Raylib.GetFPS()}", panelX + 12, panelY + 32, 14, Color.Lime);
            Raylib.DrawText($"Pos: ({camera.Position.X:F1}, {camera.Position.Y:F1}, {camera.Position.Z:F1})", panelX + 12, panelY + 50, 14, Color.White);
            Raylib.DrawText($"Active Chunks: {world.TotalLoadedChunks} (Render Dist: {world.RenderDistance})", panelX + 12, panelY + 70, 14, Color.SkyBlue);
            Raylib.DrawText($"Raw Quads: {world.TotalRawQuads:N0}", panelX + 12, panelY + 90, 14, Color.LightGray);
            Raylib.DrawText($"Greedy Quads: {world.TotalGreedyQuads:N0}", panelX + 12, panelY + 110, 14, Color.Yellow);
            Raylib.DrawText($"Quad Reduction: {world.QuadReductionPercentage:F1}% saved!", panelX + 12, panelY + 130, 15, Color.Green);
            Raylib.DrawText("Ambient Occlusion: ON (4-Corner AO)", panelX + 12, panelY + 152, 14, Color.Orange);

            Raylib.DrawText("[WASD/Space/Ctrl] Move Camera  |  [ESC] Menu", 10, Raylib.GetScreenHeight() - 25, 14, Color.White);
        }

        private static void DrawBackground(int width, int height, float[] px, float[] py, int count, int frame)
        {
            Color gridColor = new Color((byte)0, (byte)150, (byte)255, (byte)30);
            int gridSize = 40;
            for (int x = 0; x < width; x += gridSize)
            {
                Raylib.DrawLine(x, 0, x, height, gridColor);
            }
            for (int y = 0; y < height; y += gridSize)
            {
                Raylib.DrawLine(0, y, width, y, gridColor);
            }

            for (int i = 0; i < count; i++)
            {
                py[i] += (float)(Math.Sin(frame * 0.05 + i) * 0.5 + 1.0);
                if (py[i] > height)
                {
                    py[i] = 0;
                    px[i] = new Random().Next(0, width);
                }
                float alpha = (float)(Math.Sin(frame * 0.05 + i) * 0.4 + 0.6);
                Color particleColor = new Color((byte)0, (byte)210, (byte)255, (byte)(alpha * 200));
                Raylib.DrawCircleV(new Vector2(px[i], py[i]), 2.5f, particleColor);
            }
        }

        public static void SaveScreenshot(string fileName)
        {
            string relDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshot");
            if (!Directory.Exists(relDir))
            {
                Directory.CreateDirectory(relDir);
            }

            string relPath = Path.Combine("screenshot", fileName);
            Raylib.TakeScreenshot(relPath);
            Console.WriteLine($"[Screenshot] Saved relative screenshot to: {relPath}");

            string rootDir = "/screenshot";
            try
            {
                if (!Directory.Exists(rootDir))
                {
                    Directory.CreateDirectory(rootDir);
                }
                string rootPath = Path.Combine(rootDir, fileName);

                if (File.Exists(relPath))
                {
                    File.Copy(relPath, rootPath, true);
                    Console.WriteLine($"[Screenshot] Copied screenshot to: {rootPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Screenshot Note] System /screenshot directory note: {ex.Message}");
            }
        }
    }
}
