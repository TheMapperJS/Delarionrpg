using System;
using Raylib_cs;

namespace RaylibUltralightApp
{
    public enum BlockType : byte
    {
        Air = 0,
        Grass = 1,
        Dirt = 2,
        Stone = 3,
        Wood = 4,
        Leaves = 5,
        Bedrock = 6,
        Sand = 7,
        Water = 8
    }

    public enum BlockFace
    {
        Top,
        Bottom,
        Side
    }

    public static class BlockHelper
    {
        public static bool IsTransparent(BlockType type)
        {
            return type == BlockType.Air || type == BlockType.Water;
        }

        public static Color GetBlockColor(BlockType type, BlockFace face)
        {
            switch (type)
            {
                case BlockType.Grass:
                    if (face == BlockFace.Top) return new Color(76, 175, 80, 255);       // Lush Green
                    if (face == BlockFace.Bottom) return new Color(121, 85, 72, 255);    // Brown Dirt
                    return new Color(100, 140, 70, 255);                                 // Side Grass/Dirt

                case BlockType.Dirt:
                    return new Color(121, 85, 72, 255);                                   // Dirt Brown

                case BlockType.Stone:
                    return new Color(120, 130, 140, 255);                                 // Slate Stone Gray

                case BlockType.Wood:
                    if (face == BlockFace.Top || face == BlockFace.Bottom) return new Color(160, 110, 60, 255);
                    return new Color(133, 87, 35, 255);                                  // Dark Oak Wood

                case BlockType.Leaves:
                    return new Color(46, 125, 50, 255);                                  // Foliage Green

                case BlockType.Bedrock:
                    return new Color(40, 40, 45, 255);                                   // Deep Bedrock Dark

                case BlockType.Sand:
                    return new Color(225, 200, 130, 255);                                // Desert Sand

                case BlockType.Water:
                    return new Color(40, 120, 220, 180);                                 // Semi-translucent Water

                default:
                    return Color.White;
            }
        }
    }
}
