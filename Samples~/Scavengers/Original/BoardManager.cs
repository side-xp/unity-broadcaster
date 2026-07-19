#pragma warning disable IDE1006 // Naming Styles, disabled for demo
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SideXP.Broadcaster.Scavengers.Original
{
    /// <summary>
    /// Handles board configuration and generation.
    /// </summary>
    public class BoardManager : MonoBehaviour
    {
        /// <summary>
        /// Utility class for storing a minimum and maximum value. Serializable so it's editable in the inspector.
        /// </summary>
        [Serializable]
        public class Count
        {
            public int minimum;
            public int maximum;

            public Count(int min, int max)
            {
                minimum = min;
                maximum = max;
            }
        }

        [Header("Board settings")]

        [Tooltip("Number of columns in the game board.")]
        public int columns = 8;

        [Tooltip("Number of rows in the game board.")]
        public int rows = 8;

        [Tooltip("Lower and upper limit for the random number of walls per level.")]
        public Count wallCount = new Count(5, 9);

        [Tooltip("Lower and upper limit for the random number of food items per level.")]
        public Count foodCount = new Count(1, 5);

        [Header("Prefabs")]

        public GameObject exit;
        public GameObject[] floorTiles;
        public GameObject[] wallTiles;
        public GameObject[] foodTiles;
        public GameObject[] enemyTiles;
        public GameObject[] outerWallTiles;

        /// <summary>Parent of the prefabs instantiated in the board.</summary>
        private Transform boardHolder;
        /// <summary>A temporary list used during the board generation to track the possible positions for placing tiles.</summary>
        private List<Vector3> gridPositions = new List<Vector3>();

        /// <summary>
        /// Clears <see cref="gridPositions"/> and prepares to generate a new board.
        /// </summary>
        private void InitialiseList()
        {
            gridPositions.Clear();
            for (int x = 1; x < columns - 1; x++)
            {
                for (int y = 1; y < rows - 1; y++)
                {
                    gridPositions.Add(new Vector3(x, y, 0f));
                }
            }
        }

        /// <summary>
        /// Spawns the outer walls and walkable tiles of the board.
        /// </summary>
        private void BoardSetup()
        {
            // Create the parent object for spawned prefabs
            boardHolder = new GameObject("Board").transform;

            // -1/+1 offsets are for the outer walls
            for (int x = -1; x < columns + 1; x++)
            {
                for (int y = -1; y < rows + 1; y++)
                {
                    // Pick random floor tile
                    GameObject toInstantiate = floorTiles[Random.Range(0, floorTiles.Length)];

                    // Switch to random outer wall tile if the position is out of board range
                    if (x == -1 || x == columns || y == -1 || y == rows)
                        toInstantiate = outerWallTiles[Random.Range(0, outerWallTiles.Length)];

                    // Spawn tile
                    GameObject instance = Instantiate(toInstantiate, new Vector3(x, y, 0f), Quaternion.identity) as GameObject;
                    instance.transform.SetParent(boardHolder);
                }
            }
        }

        /// <summary>
        /// Gets a random position among the remaining possible ones.
        /// </summary>
        private Vector3 RandomPosition()
        {
            int randomIndex = Random.Range(0, gridPositions.Count);
            Vector3 randomPosition = gridPositions[randomIndex];
            gridPositions.RemoveAt(randomIndex);
            return randomPosition;
        }


        /// <summary>
        /// Spawns a random number of given tiles (between given <paramref name="minimum"/> and <paramref name="maximum"/>) on the board,
        /// at a random position among the remaining possible ones.
        /// </summary>
        private void LayoutObjectAtRandom(GameObject[] tileArray, int minimum, int maximum)
        {
            int objectCount = Random.Range(minimum, maximum + 1);

            for (int i = 0; i < objectCount; i++)
            {
                Vector3 randomPosition = RandomPosition();
                GameObject tileChoice = tileArray[Random.Range(0, tileArray.Length)];
                Instantiate(tileChoice, randomPosition, Quaternion.identity);
            }
        }

        /// <summary>
        /// Sets up the board in the scene given the current level count.
        /// </summary>
        public void SetupScene(int level)
        {
            // Spawn outer walls and floor
            BoardSetup();
            // Reset available positions list
            InitialiseList();
            // Spawn walls
            LayoutObjectAtRandom(wallTiles, wallCount.minimum, wallCount.maximum);
            // Spawn food
            LayoutObjectAtRandom(foodTiles, foodCount.minimum, foodCount.maximum);

            // Spawn enemies, which count is based on a logarithmic progression depending on the level count
            int enemyCount = (int)Mathf.Log(level, 2f);
            LayoutObjectAtRandom(enemyTiles, enemyCount, enemyCount);

            // Spawn exit tile
            Instantiate(exit, new Vector3(columns - 1, rows - 1, 0f), Quaternion.identity);
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
