using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace RaylibUltralightApp
{
    public class Chunk
    {
        public const int SIZE_X = 16;
        public const int SIZE_Y = 32;
        public const int SIZE_Z = 16;

        public int ChunkX { get; }
        public int ChunkZ { get; }

        private readonly BlockType[,,] voxels = new BlockType[SIZE_X, SIZE_Y, SIZE_Z];
        private readonly World? world;

        public Mesh Mesh { get; private set; }
        public Model Model { get; private set; }
        public bool IsLoaded { get; private set; }

        public int RawQuadCount { get; private set; }
        public int GreedyQuadCount { get; private set; }

        public Chunk(int chunkX, int chunkZ, World? world = null)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            this.world = world;
            GenerateTerrain();
            BuildMesh();
        }

        public static BlockType GetTerrainBlock(int wx, int wy, int wz)
        {
            if (wy < 0 || wy >= SIZE_Y) return BlockType.Air;
            if (wy == 0) return BlockType.Bedrock;

            float heightVal = 14.0f + (float)(
                Math.Sin(wx * 0.07f) * 5.0f +
                Math.Cos(wz * 0.07f) * 5.0f +
                Math.Sin((wx + wz) * 0.12f) * 3.0f
            );

            int height = Math.Clamp((int)heightVal, 3, SIZE_Y - 6);

            if (wy < height - 3) return BlockType.Stone;
            if (wy < height) return BlockType.Dirt;
            if (wy == height) return (height <= 9) ? BlockType.Sand : BlockType.Grass;
            if (wy <= 9 && height <= 9) return BlockType.Water;

            return BlockType.Air;
        }

        private void GenerateTerrain()
        {
            int worldXOffset = ChunkX * SIZE_X;
            int worldZOffset = ChunkZ * SIZE_Z;

            for (int x = 0; x < SIZE_X; x++)
            {
                int wx = worldXOffset + x;
                for (int z = 0; z < SIZE_Z; z++)
                {
                    int wz = worldZOffset + z;

                    // Compute height using overlapping sine waves
                    float heightVal = 14.0f + (float)(
                        Math.Sin(wx * 0.07f) * 5.0f +
                        Math.Cos(wz * 0.07f) * 5.0f +
                        Math.Sin((wx + wz) * 0.12f) * 3.0f
                    );

                    int height = Math.Clamp((int)heightVal, 3, SIZE_Y - 6);

                    for (int y = 0; y < SIZE_Y; y++)
                    {
                        if (y == 0)
                        {
                            voxels[x, y, z] = BlockType.Bedrock;
                        }
                        else if (y < height - 3)
                        {
                            voxels[x, y, z] = BlockType.Stone;
                        }
                        else if (y < height)
                        {
                            voxels[x, y, z] = BlockType.Dirt;
                        }
                        else if (y == height)
                        {
                            voxels[x, y, z] = (height <= 9) ? BlockType.Sand : BlockType.Grass;
                        }
                        else if (y <= 9 && height <= 9)
                        {
                            voxels[x, y, z] = BlockType.Water;
                        }
                        else
                        {
                            voxels[x, y, z] = BlockType.Air;
                        }
                    }

                    // Tree generation on grass with deterministic placement
                    if (height > 9 && height < SIZE_Y - 7)
                    {
                        int hash = Math.Abs((wx * 73856093) ^ (wz * 19349663));
                        if (hash % 47 == 0 && x >= 2 && x <= SIZE_X - 3 && z >= 2 && z <= SIZE_Z - 3)
                        {
                            int trunkHeight = 4;
                            for (int ty = 1; ty <= trunkHeight; ty++)
                            {
                                if (height + ty < SIZE_Y)
                                    voxels[x, height + ty, z] = BlockType.Wood;
                            }

                            // Leaves canopy
                            int leafBase = height + trunkHeight - 1;
                            for (int lx = -2; lx <= 2; lx++)
                            {
                                for (int lz = -2; lz <= 2; lz++)
                                {
                                    for (int ly = 0; ly <= 2; ly++)
                                    {
                                        if (Math.Abs(lx) == 2 && Math.Abs(lz) == 2 && ly == 2) continue;
                                        int px = x + lx;
                                        int pz = z + lz;
                                        int py = leafBase + ly;

                                        if (px >= 0 && px < SIZE_X && pz >= 0 && pz < SIZE_Z && py < SIZE_Y)
                                        {
                                            if (voxels[px, py, pz] == BlockType.Air)
                                            {
                                                voxels[px, py, pz] = BlockType.Leaves;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public BlockType GetBlock(int x, int y, int z)
        {
            if (x >= 0 && x < SIZE_X && y >= 0 && y < SIZE_Y && z >= 0 && z < SIZE_Z)
            {
                return voxels[x, y, z];
            }

            int worldX = ChunkX * SIZE_X + x;
            int worldZ = ChunkZ * SIZE_Z + z;

            if (world != null)
            {
                return world.GetBlock(worldX, y, worldZ);
            }

            return GetTerrainBlock(worldX, y, worldZ);
        }

        public bool IsOpaque(int x, int y, int z)
        {
            return !BlockHelper.IsTransparent(GetBlock(x, y, z));
        }

        private byte CalculateVertexAO(bool side1, bool side2, bool corner)
        {
            if (side1 && side2) return 0; // Fully occluded by 2 side blocks
            int count = (side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0);
            return (byte)(3 - count);
        }

        public (byte ao0, byte ao1, byte ao2, byte ao3) GetFaceAO(int x, int y, int z, int dir)
        {
            byte ao0, ao1, ao2, ao3;

            switch (dir)
            {
                case 0: // -Z
                    // v0: (-X, -Y), v1: (-X, +Y), v2: (+X, +Y), v3: (+X, -Y)
                    ao0 = CalculateVertexAO(IsOpaque(x - 1, y, z - 1), IsOpaque(x, y - 1, z - 1), IsOpaque(x - 1, y - 1, z - 1));
                    ao1 = CalculateVertexAO(IsOpaque(x - 1, y, z - 1), IsOpaque(x, y + 1, z - 1), IsOpaque(x - 1, y + 1, z - 1));
                    ao2 = CalculateVertexAO(IsOpaque(x + 1, y, z - 1), IsOpaque(x, y + 1, z - 1), IsOpaque(x + 1, y + 1, z - 1));
                    ao3 = CalculateVertexAO(IsOpaque(x + 1, y, z - 1), IsOpaque(x, y - 1, z - 1), IsOpaque(x + 1, y - 1, z - 1));
                    break;

                case 1: // +Z
                    // v0: (-X, -Y), v1: (+X, -Y), v2: (+X, +Y), v3: (-X, +Y)
                    ao0 = CalculateVertexAO(IsOpaque(x - 1, y, z + 1), IsOpaque(x, y - 1, z + 1), IsOpaque(x - 1, y - 1, z + 1));
                    ao1 = CalculateVertexAO(IsOpaque(x + 1, y, z + 1), IsOpaque(x, y - 1, z + 1), IsOpaque(x + 1, y - 1, z + 1));
                    ao2 = CalculateVertexAO(IsOpaque(x + 1, y, z + 1), IsOpaque(x, y + 1, z + 1), IsOpaque(x + 1, y + 1, z + 1));
                    ao3 = CalculateVertexAO(IsOpaque(x - 1, y, z + 1), IsOpaque(x, y + 1, z + 1), IsOpaque(x - 1, y + 1, z + 1));
                    break;

                case 2: // -X
                    // v0: (-Z, -Y), v1: (+Z, -Y), v2: (+Z, +Y), v3: (-Z, +Y)
                    ao0 = CalculateVertexAO(IsOpaque(x - 1, y, z - 1), IsOpaque(x - 1, y - 1, z), IsOpaque(x - 1, y - 1, z - 1));
                    ao1 = CalculateVertexAO(IsOpaque(x - 1, y, z + 1), IsOpaque(x - 1, y - 1, z), IsOpaque(x - 1, y - 1, z + 1));
                    ao2 = CalculateVertexAO(IsOpaque(x - 1, y, z + 1), IsOpaque(x - 1, y + 1, z), IsOpaque(x - 1, y + 1, z + 1));
                    ao3 = CalculateVertexAO(IsOpaque(x - 1, y, z - 1), IsOpaque(x - 1, y + 1, z), IsOpaque(x - 1, y + 1, z - 1));
                    break;

                case 3: // +X
                    // v0: (+Z, -Y), v1: (-Z, -Y), v2: (-Z, +Y), v3: (+Z, +Y)
                    ao0 = CalculateVertexAO(IsOpaque(x + 1, y, z + 1), IsOpaque(x + 1, y - 1, z), IsOpaque(x + 1, y - 1, z + 1));
                    ao1 = CalculateVertexAO(IsOpaque(x + 1, y, z - 1), IsOpaque(x + 1, y - 1, z), IsOpaque(x + 1, y - 1, z - 1));
                    ao2 = CalculateVertexAO(IsOpaque(x + 1, y, z - 1), IsOpaque(x + 1, y + 1, z), IsOpaque(x + 1, y + 1, z - 1));
                    ao3 = CalculateVertexAO(IsOpaque(x + 1, y, z + 1), IsOpaque(x + 1, y + 1, z), IsOpaque(x + 1, y + 1, z + 1));
                    break;

                case 4: // -Y
                    // v0: (-X, -Z), v1: (+X, -Z), v2: (+X, +Z), v3: (-X, +Z)
                    ao0 = CalculateVertexAO(IsOpaque(x - 1, y - 1, z), IsOpaque(x, y - 1, z - 1), IsOpaque(x - 1, y - 1, z - 1));
                    ao1 = CalculateVertexAO(IsOpaque(x + 1, y - 1, z), IsOpaque(x, y - 1, z - 1), IsOpaque(x + 1, y - 1, z - 1));
                    ao2 = CalculateVertexAO(IsOpaque(x + 1, y - 1, z), IsOpaque(x, y - 1, z + 1), IsOpaque(x + 1, y - 1, z + 1));
                    ao3 = CalculateVertexAO(IsOpaque(x - 1, y - 1, z), IsOpaque(x, y - 1, z + 1), IsOpaque(x - 1, y - 1, z + 1));
                    break;

                case 5: // +Y
                default:
                    // v0: (-X, +Z), v1: (+X, +Z), v2: (+X, -Z), v3: (-X, -Z)
                    ao0 = CalculateVertexAO(IsOpaque(x - 1, y + 1, z), IsOpaque(x, y + 1, z + 1), IsOpaque(x - 1, y + 1, z + 1));
                    ao1 = CalculateVertexAO(IsOpaque(x + 1, y + 1, z), IsOpaque(x, y + 1, z + 1), IsOpaque(x + 1, y + 1, z + 1));
                    ao2 = CalculateVertexAO(IsOpaque(x + 1, y + 1, z), IsOpaque(x, y + 1, z - 1), IsOpaque(x + 1, y + 1, z - 1));
                    ao3 = CalculateVertexAO(IsOpaque(x - 1, y + 1, z), IsOpaque(x, y + 1, z - 1), IsOpaque(x - 1, y + 1, z - 1));
                    break;
            }

            return (ao0, ao1, ao2, ao3);
        }

        private static float GetAOFactor(byte ao)
        {
            switch (ao)
            {
                case 0: return 0.40f;
                case 1: return 0.60f;
                case 2: return 0.80f;
                case 3:
                default: return 1.00f;
            }
        }

        private struct MaskCell
        {
            public BlockType Block;
            public byte AO0;
            public byte AO1;
            public byte AO2;
            public byte AO3;

            public bool Equals(MaskCell other)
            {
                return Block == other.Block &&
                       AO0 == other.AO0 &&
                       AO1 == other.AO1 &&
                       AO2 == other.AO2 &&
                       AO3 == other.AO3;
            }
        }

        private struct Quad
        {
            public Vector3 Position; // Min corner (x,y,z)
            public float Width;      // u dimension length
            public float Height;     // v dimension length
            public int Direction;    // 0: -Z, 1: +Z, 2: -X, 3: +X, 4: -Y, 5: +Y
            public BlockType Block;
            public byte AO0;
            public byte AO1;
            public byte AO2;
            public byte AO3;
        }

        private unsafe void BuildMesh()
        {
            List<Quad> quads = RunGreedyMesher();
            GreedyQuadCount = quads.Count;

            if (quads.Count == 0)
            {
                IsLoaded = false;
                return;
            }

            int vertexCount = quads.Count * 4;
            int triangleCount = quads.Count * 2;

            float* vertices = (float*)Raylib.MemAlloc((uint)(vertexCount * 3 * sizeof(float)));
            float* normals = (float*)Raylib.MemAlloc((uint)(vertexCount * 3 * sizeof(float)));
            byte* colors = (byte*)Raylib.MemAlloc((uint)(vertexCount * 4 * sizeof(byte)));
            ushort* indices = (ushort*)Raylib.MemAlloc((uint)(triangleCount * 3 * sizeof(ushort)));

            int vIdx = 0;
            int nIdx = 0;
            int cIdx = 0;
            int iIdx = 0;

            float worldX = ChunkX * SIZE_X;
            float worldZ = ChunkZ * SIZE_Z;

            for (int q = 0; q < quads.Count; q++)
            {
                Quad quad = quads[q];
                ushort baseVertIndex = (ushort)(q * 4);

                BlockFace face;
                Vector3 normal;
                float shade = 1.0f;

                switch (quad.Direction)
                {
                    case 0: // -Z
                        face = BlockFace.Side;
                        normal = new Vector3(0, 0, -1);
                        shade = 0.85f;
                        break;
                    case 1: // +Z
                        face = BlockFace.Side;
                        normal = new Vector3(0, 0, 1);
                        shade = 0.85f;
                        break;
                    case 2: // -X
                        face = BlockFace.Side;
                        normal = new Vector3(-1, 0, 0);
                        shade = 0.70f;
                        break;
                    case 3: // +X
                        face = BlockFace.Side;
                        normal = new Vector3(1, 0, 0);
                        shade = 0.70f;
                        break;
                    case 4: // -Y
                        face = BlockFace.Bottom;
                        normal = new Vector3(0, -1, 0);
                        shade = 0.50f;
                        break;
                    case 5: // +Y
                    default:
                        face = BlockFace.Top;
                        normal = new Vector3(0, 1, 0);
                        shade = 1.0f;
                        break;
                }

                Color baseCol = BlockHelper.GetBlockColor(quad.Block, face);
                baseCol.R = (byte)Math.Clamp((int)(baseCol.R * shade), 0, 255);
                baseCol.G = (byte)Math.Clamp((int)(baseCol.G * shade), 0, 255);
                baseCol.B = (byte)Math.Clamp((int)(baseCol.B * shade), 0, 255);

                // Define 4 quad vertices
                Vector3 v0, v1, v2, v3;

                float x = worldX + quad.Position.X;
                float y = quad.Position.Y;
                float z = worldZ + quad.Position.Z;
                float w = quad.Width;
                float h = quad.Height;

                switch (quad.Direction)
                {
                    case 0: // -Z
                        v0 = new Vector3(x, y, z);
                        v1 = new Vector3(x, y + h, z);
                        v2 = new Vector3(x + w, y + h, z);
                        v3 = new Vector3(x + w, y, z);
                        break;
                    case 1: // +Z
                        v0 = new Vector3(x, y, z + 1);
                        v1 = new Vector3(x + w, y, z + 1);
                        v2 = new Vector3(x + w, y + h, z + 1);
                        v3 = new Vector3(x, y + h, z + 1);
                        break;
                    case 2: // -X
                        v0 = new Vector3(x, y, z);
                        v1 = new Vector3(x, y, z + w);
                        v2 = new Vector3(x, y + h, z + w);
                        v3 = new Vector3(x, y + h, z);
                        break;
                    case 3: // +X
                        v0 = new Vector3(x + 1, y, z + w);
                        v1 = new Vector3(x + 1, y, z);
                        v2 = new Vector3(x + 1, y + h, z);
                        v3 = new Vector3(x + 1, y + h, z + w);
                        break;
                    case 4: // -Y
                        v0 = new Vector3(x, y, z);
                        v1 = new Vector3(x + w, y, z);
                        v2 = new Vector3(x + w, y, z + h);
                        v3 = new Vector3(x, y, z + h);
                        break;
                    case 5: // +Y
                    default:
                        v0 = new Vector3(x, y + 1, z + h);
                        v1 = new Vector3(x + w, y + 1, z + h);
                        v2 = new Vector3(x + w, y + 1, z);
                        v3 = new Vector3(x, y + 1, z);
                        break;
                }

                Vector3[] verts = new Vector3[] { v0, v1, v2, v3 };
                byte[] aoValues = new byte[] { quad.AO0, quad.AO1, quad.AO2, quad.AO3 };

                for (int i = 0; i < 4; i++)
                {
                    vertices[vIdx++] = verts[i].X;
                    vertices[vIdx++] = verts[i].Y;
                    vertices[vIdx++] = verts[i].Z;

                    normals[nIdx++] = normal.X;
                    normals[nIdx++] = normal.Y;
                    normals[nIdx++] = normal.Z;

                    float aoMult = GetAOFactor(aoValues[i]);
                    colors[cIdx++] = (byte)Math.Clamp((int)(baseCol.R * aoMult), 0, 255);
                    colors[cIdx++] = (byte)Math.Clamp((int)(baseCol.G * aoMult), 0, 255);
                    colors[cIdx++] = (byte)Math.Clamp((int)(baseCol.B * aoMult), 0, 255);
                    colors[cIdx++] = baseCol.A;
                }

                // Quad anisotropy diagonal flipping: flip diagonal if ao0 + ao2 < ao1 + ao3
                if (quad.AO0 + quad.AO2 < quad.AO1 + quad.AO3)
                {
                    indices[iIdx++] = (ushort)(baseVertIndex + 0);
                    indices[iIdx++] = (ushort)(baseVertIndex + 1);
                    indices[iIdx++] = (ushort)(baseVertIndex + 3);

                    indices[iIdx++] = (ushort)(baseVertIndex + 1);
                    indices[iIdx++] = (ushort)(baseVertIndex + 2);
                    indices[iIdx++] = (ushort)(baseVertIndex + 3);
                }
                else
                {
                    indices[iIdx++] = (ushort)(baseVertIndex + 0);
                    indices[iIdx++] = (ushort)(baseVertIndex + 1);
                    indices[iIdx++] = (ushort)(baseVertIndex + 2);

                    indices[iIdx++] = (ushort)(baseVertIndex + 0);
                    indices[iIdx++] = (ushort)(baseVertIndex + 2);
                    indices[iIdx++] = (ushort)(baseVertIndex + 3);
                }
            }

            Mesh mesh = new Mesh
            {
                VertexCount = vertexCount,
                TriangleCount = triangleCount,
                Vertices = vertices,
                Normals = normals,
                Colors = colors,
                Indices = indices
            };

            Raylib.UploadMesh(ref mesh, false);
            Mesh = mesh;
            Model = Raylib.LoadModelFromMesh(mesh);
            IsLoaded = true;
        }

        private List<Quad> RunGreedyMesher()
        {
            List<Quad> quads = new List<Quad>();
            int rawCount = 0;

            // Sweep through all 6 faces
            for (int dir = 0; dir < 6; dir++)
            {
                int mainAxisMax = (dir == 2 || dir == 3) ? SIZE_X : ((dir == 4 || dir == 5) ? SIZE_Y : SIZE_Z);
                int uMax = (dir == 2 || dir == 3) ? SIZE_Z : SIZE_X;
                int vMax = (dir == 4 || dir == 5) ? SIZE_Z : SIZE_Y;

                for (int slice = 0; slice < mainAxisMax; slice++)
                {
                    MaskCell[,] mask = new MaskCell[uMax, vMax];

                    // Populate mask for current slice
                    for (int u = 0; u < uMax; u++)
                    {
                        for (int v = 0; v < vMax; v++)
                        {
                            int x = 0, y = 0, z = 0;
                            int nx = 0, ny = 0, nz = 0;

                            GetSliceCoordinates(dir, slice, u, v, out x, out y, out z, out nx, out ny, out nz);

                            BlockType currentBlock = voxels[x, y, z];

                            if (currentBlock != BlockType.Air)
                            {
                                BlockType neighborBlock = GetBlock(nx, ny, nz);
                                bool neighborTransparent = BlockHelper.IsTransparent(neighborBlock);

                                // Special case for water: don't render water-to-water internal faces
                                if (currentBlock == BlockType.Water && neighborBlock == BlockType.Water)
                                {
                                    neighborTransparent = false;
                                }

                                if (neighborTransparent)
                                {
                                    var (ao0, ao1, ao2, ao3) = GetFaceAO(x, y, z, dir);
                                    mask[u, v] = new MaskCell
                                    {
                                        Block = currentBlock,
                                        AO0 = ao0,
                                        AO1 = ao1,
                                        AO2 = ao2,
                                        AO3 = ao3
                                    };
                                    rawCount++;
                                }
                            }
                        }
                    }

                    // Greedy merge on 2D mask
                    for (int v = 0; v < vMax; v++)
                    {
                        for (int u = 0; u < uMax; u++)
                        {
                            MaskCell cell = mask[u, v];
                            if (cell.Block == BlockType.Air) continue;

                            // Compute width w
                            int w = 1;
                            while (u + w < uMax && mask[u + w, v].Equals(cell))
                            {
                                w++;
                            }

                            // Compute height h
                            int h = 1;
                            bool canExpand = true;
                            while (v + h < vMax && canExpand)
                            {
                                for (int i = 0; i < w; i++)
                                {
                                    if (!mask[u + i, v + h].Equals(cell))
                                    {
                                        canExpand = false;
                                        break;
                                    }
                                }
                                if (canExpand) h++;
                            }

                            // Store merged quad position in chunk coordinates
                            int startX = 0, startY = 0, startZ = 0;
                            int dummy1 = 0, dummy2 = 0, dummy3 = 0;
                            GetSliceCoordinates(dir, slice, u, v, out startX, out startY, out startZ, out dummy1, out dummy2, out dummy3);

                            quads.Add(new Quad
                            {
                                Position = new Vector3(startX, startY, startZ),
                                Width = w,
                                Height = h,
                                Direction = dir,
                                Block = cell.Block,
                                AO0 = cell.AO0,
                                AO1 = cell.AO1,
                                AO2 = cell.AO2,
                                AO3 = cell.AO3
                            });

                            // Clear mask for merged cells
                            for (int j = 0; j < h; j++)
                            {
                                for (int i = 0; i < w; i++)
                                {
                                    mask[u + i, v + j] = default;
                                }
                            }

                            u += w - 1;
                        }
                    }
                }
            }

            RawQuadCount = rawCount;
            return quads;
        }

        private static void GetSliceCoordinates(int dir, int slice, int u, int v,
            out int x, out int y, out int z,
            out int nx, out int ny, out int nz)
        {
            switch (dir)
            {
                case 0: // -Z
                    x = u; y = v; z = slice;
                    nx = x; ny = y; nz = z - 1;
                    break;
                case 1: // +Z
                    x = u; y = v; z = slice;
                    nx = x; ny = y; nz = z + 1;
                    break;
                case 2: // -X
                    x = slice; y = v; z = u;
                    nx = x - 1; ny = y; nz = z;
                    break;
                case 3: // +X
                    x = slice; y = v; z = u;
                    nx = x + 1; ny = y; nz = z;
                    break;
                case 4: // -Y
                    x = u; y = slice; z = v;
                    nx = x; ny = y - 1; nz = z;
                    break;
                case 5: // +Y
                default:
                    x = u; y = slice; z = v;
                    nx = x; ny = y + 1; nz = z;
                    break;
            }
        }

        public void Unload()
        {
            if (IsLoaded)
            {
                Raylib.UnloadModel(Model); // UnloadModel also unloads attached mesh in Raylib
                IsLoaded = false;
            }
        }
    }
}
