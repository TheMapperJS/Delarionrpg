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

        static unsafe void Main(string[] args)
        {
            bool screenshotArg = Array.Exists(args, arg => arg.Equals("--screenshot", StringComparison.OrdinalIgnoreCase) || arg.Equals("-s", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine("Initializing Raylib + Ultralight.NET Voxel World Engine...");

            int width = 1024;
            int height = 768;

            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
            Raylib.InitWindow(width, height, "CYBER QUEST - Greedy Meshed Voxel World Engine");
            Raylib.SetTargetFPS(60);

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

            // Load Menu HTML
            string menuHtmlPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "menu.html");
            if (File.Exists(menuHtmlPath))
            {
                view.HTML = File.ReadAllText(menuHtmlPath);
            }
            else
            {
                Console.WriteLine("Warning: assets/menu.html not found, fallback to default HTML.");
                view.HTML = "<html><body style='color:white;background:rgba(0,0,0,0.8);'><h1>VOXEL QUEST</h1><button onclick='console.log(\"action:start\")'>Start</button></body></html>";
            }

            bool isPageLoaded = false;
            view.OnFinishLoading += (frameId, isMainFrame, url) =>
            {
                isPageLoaded = true;
                Console.WriteLine("Ultralight page loading finished.");
            };

            // Wait brief moment for initial DOM ready
            int loadWaitCounter = 0;
            while (!isPageLoaded && loadWaitCounter < 100)
            {
                renderer.Update();
                Thread.Sleep(10);
                loadWaitCounter++;
            }

            // Create Raylib texture for UI rendering
            Image uiImage = Raylib.GenImageColor(width, height, Color.Blank);
            Raylib.ImageFormat(ref uiImage, PixelFormat.UncompressedR8G8B8A8);
            Texture2D uiTexture = Raylib.LoadTextureFromImage(uiImage);
            Raylib.UnloadImage(uiImage);

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

            // Automatically enter Playing mode when --screenshot is passed to showcase 3D Voxel world
            if (screenshotArg)
            {
                currentState = AppState.Playing;
            }

            // Main Application Loop
            while (!Raylib.WindowShouldClose() && !shouldExit)
            {
                frameCount++;

                // 1. Process UI Inputs
                int mouseX = Raylib.GetMouseX();
                int mouseY = Raylib.GetMouseY();

                if (currentState == AppState.Playing)
                {
                    // Forward Mouse Events to Ultralight RPG HUD
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
                }
                else if (currentState == AppState.MainMenu)
                {
                    if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space) || Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        currentState = AppState.Playing;
                    }
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
                        currentState = AppState.MainMenu;
                    }
                    else
                    {
                        currentState = AppState.Playing;
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

                    // Draw RPG HUD Overlay (Ultralight HTML UI)
                    Raylib.DrawTexture(uiTexture, 0, 0, Color.White);

                    // Draw Voxel HUD Overlay
                    DrawHUD(world, camera);
                }
                else
                {
                    // Main Menu Background Particles & UI
                    Raylib.ClearBackground(new Color(15, 20, 32, 255));
                    DrawBackground(width, height, particleX, particleY, particleCount, frameCount);
                    DrawMainMenu(width, height);
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

        private static void HandleUIAction(string action, View view)
        {
            if (action == "start")
            {
                Console.WriteLine("[RPG UI] 'Enter Voxel Realm' clicked!");
                currentState = AppState.Playing;
            }
            else if (action == "exit")
            {
                Console.WriteLine("[RPG UI] 'Abandon Realm' clicked! Returning to Main Menu.");
                currentState = AppState.MainMenu;
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

        private static void DrawMainMenu(int width, int height)
        {
            string title = "CHRONICLES OF AETHELGARD";
            string subtitle = "Voxel Realm Engine";
            string prompt = "Press [ENTER] or CLICK to Enter Voxel Realm";

            int titleSize = 40;
            int titleWidth = Raylib.MeasureText(title, titleSize);
            Raylib.DrawText(title, (width - titleWidth) / 2, height / 3, titleSize, Color.Gold);

            int subSize = 20;
            int subWidth = Raylib.MeasureText(subtitle, subSize);
            Raylib.DrawText(subtitle, (width - subWidth) / 2, height / 3 + 55, subSize, Color.SkyBlue);

            int promptSize = 18;
            int promptWidth = Raylib.MeasureText(prompt, promptSize);
            byte alpha = (byte)(180 + Math.Sin(Raylib.GetTime() * 4.0) * 75);
            Color promptColor = new Color((byte)255, (byte)215, (byte)0, alpha);
            Raylib.DrawText(prompt, (width - promptWidth) / 2, height / 2 + 80, promptSize, promptColor);
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
