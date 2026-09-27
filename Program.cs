using System;
using System.IO;
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

            Console.WriteLine("Initializing Raylib + Ultralight.NET App...");

            int width = 800;
            int height = 600;

            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
            Raylib.InitWindow(width, height, "CYBER QUEST - Raylib-cs + Ultralight.NET Start Menu");
            Raylib.SetTargetFPS(60);

            // Initialize UltralightNet
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
                view.HTML = "<html><body style='color:white;background:rgba(0,0,0,0.8);'><h1>CYBER QUEST</h1><button onclick='console.log(\"action:start\")'>Start</button></body></html>";
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

            // Particle system variables for background
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

                // 1. Process Inputs
                int mouseX = Raylib.GetMouseX();
                int mouseY = Raylib.GetMouseY();

                // Forward Mouse Position
                view.FireMouseEvent(new ULMouseEvent
                {
                    Type = ULMouseEventType.MouseMoved,
                    X = mouseX,
                    Y = mouseY,
                    Button = ULMouseEventButton.None
                });

                // Forward Mouse Buttons
                if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    view.FireMouseEvent(new ULMouseEvent { Type = ULMouseEventType.MouseDown, X = mouseX, Y = mouseY, Button = ULMouseEventButton.Left });
                }
                if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                {
                    view.FireMouseEvent(new ULMouseEvent { Type = ULMouseEventType.MouseUp, X = mouseX, Y = mouseY, Button = ULMouseEventButton.Left });
                }
                if (Raylib.IsMouseButtonPressed(MouseButton.Right))
                {
                    view.FireMouseEvent(new ULMouseEvent { Type = ULMouseEventType.MouseDown, X = mouseX, Y = mouseY, Button = ULMouseEventButton.Right });
                }
                if (Raylib.IsMouseButtonReleased(MouseButton.Right))
                {
                    view.FireMouseEvent(new ULMouseEvent { Type = ULMouseEventType.MouseUp, X = mouseX, Y = mouseY, Button = ULMouseEventButton.Right });
                }

                // Forward Mouse Scroll
                float wheel = Raylib.GetMouseWheelMove();
                if (wheel != 0)
                {
                    view.FireScrollEvent(new ULScrollEvent
                    {
                        Type = ULScrollEventType.ByPixel,
                        DeltaX = 0,
                        DeltaY = (int)(wheel * 100)
                    });
                }

                // Global Hotkeys
                if (Raylib.IsKeyPressed(KeyboardKey.F12) || Raylib.IsKeyPressed(KeyboardKey.S))
                {
                    SaveScreenshot("screenshot_manual.png");
                }
                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    if (currentState == AppState.Playing)
                    {
                        currentState = AppState.MainMenu;
                    }
                }

                // 2. Update Ultralight View
                renderer.Update();
                renderer.Render();

                // 3. Sync Ultralight Surface to Raylib Texture
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

                // 4. Update Background Particles
                for (int i = 0; i < particleCount; i++)
                {
                    particleY[i] += particleSpeed[i];
                    if (particleY[i] > height)
                    {
                        particleY[i] = 0;
                        particleX[i] = rand.Next(0, width);
                    }
                }

                // 5. Draw Frame
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(15, 20, 32, 255));

                // Draw Raylib Animated Background (Grid + Particles)
                DrawBackground(width, height, particleX, particleY, particleCount, frameCount);

                if (currentState == AppState.Playing)
                {
                    // Draw active gameplay scene
                    DrawGameScene(width, height, frameCount);
                }

                // Draw Ultralight UI Overlay
                Raylib.DrawTexture(uiTexture, 0, 0, Color.White);

                Raylib.EndDrawing();

                // Handle CLI --screenshot flag
                if (screenshotArg && frameCount >= 15)
                {
                    Console.WriteLine("[CLI] Taking screenshot requested via --screenshot command...");
                    SaveScreenshot("screenshot.png");
                    shouldExit = true;
                }
            }

            // Cleanup
            Raylib.UnloadTexture(uiTexture);
            Raylib.CloseWindow();
            Console.WriteLine("Application exited cleanly.");
        }

        private static void HandleUIAction(string action, View view)
        {
            if (action == "start")
            {
                Console.WriteLine("Start Game button clicked! Switching state to Playing.");
                currentState = AppState.Playing;
            }
            else if (action == "exit")
            {
                Console.WriteLine("Exit button clicked! Closing application.");
                shouldExit = true;
            }
            else if (action.StartsWith("volume:"))
            {
                string volStr = action.Substring("volume:".Length);
                Console.WriteLine($"Volume changed to: {volStr}%");
            }
        }

        private static void DrawBackground(int width, int height, float[] px, float[] py, int count, int frame)
        {
            // Draw grid lines
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

            // Draw floating particles
            for (int i = 0; i < count; i++)
            {
                float alpha = (float)(Math.Sin(frame * 0.05 + i) * 0.4 + 0.6);
                Color particleColor = new Color((byte)0, (byte)210, (byte)255, (byte)(alpha * 200));
                Raylib.DrawCircleV(new System.Numerics.Vector2(px[i], py[i]), 2.5f, particleColor);
            }
        }

        private static void DrawGameScene(int width, int height, int frame)
        {
            // Render 3D/2D Game world elements behind UI
            float centerX = width / 2f;
            float centerY = height / 2f + 50f;

            double time = frame * 0.03;
            float posX = centerX + (float)Math.Cos(time) * 120f;
            float posY = centerY + (float)Math.Sin(time) * 60f;

            Raylib.DrawCircle((int)posX, (int)posY, 25, Color.Gold);
            Raylib.DrawText("GAME ACTIVE! Press ESC for Main Menu", 210, 520, 20, Color.Lime);
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

            // Also check root /screenshot directory
            string rootDir = "/screenshot";
            try
            {
                if (!Directory.Exists(rootDir))
                {
                    Directory.CreateDirectory(rootDir);
                }
                string rootPath = Path.Combine(rootDir, fileName);

                // Copy file to /screenshot/ if created
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
