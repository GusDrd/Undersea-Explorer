using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Tilemaps;
using UnityMovementAI;

public class LevelGenerator : MonoBehaviour
{
    /** -------------------
     * 
     * Thanks to the following sources which provided much help to create the generation algorithm:
     * 
     * Thanks to Bronson Zgeb - https://bronsonzgeb.com/index.php/2022/01/30/procedural-generation-with-cellular-automata/
     * And Roguebasin - http://www.roguebasin.com/index.php/Cellular_Automata_Method_for_Generating_Random_Cave-Like_Levels#C.23_Code
     * For help with the cellular automata's base implementation.
     * 
     * Thanks GeeksForGeeks - https://www.geeksforgeeks.org/flood-fill-algorithm/
     * For help with the Flood fill algorithm's implementation.
     * 
     * ------------------------- */


    // Cellular automata map variables
    private int[,] _caMap;
    private List<Coordinates> _openTiles;

    public int _width;
    public int _height;

    // Generation variables
    public float _startFill;

    public int _r1Limit;
    public int _r2Limit;

    public int _mainSteps;
    public int _smoothSteps;

    // Tilemap variables
    public Tilemap _wallMap;
    public Tilemap _groundMap;

    public Tile groundTile;
    public Tile wallTile;

    // Entity spawns
    public GameObject treasure;
    public GameObject diver;
    public GameObject mermaid;
    public GameObject shark;
    public GameObject mine;

    [HideInInspector] public static List<GameObject> _explosiveMines;


    // Path variables for agent navigation
    private List<Node> _navMap;
    [HideInInspector] public static LinePath _mermaidPath;
    [HideInInspector] public static LinePath _treasurePath;
    [HideInInspector] public static LinePath _sharkPath;
    [HideInInspector] public static LinePath _minePath;

    void OnEnable()
    {
        _explosiveMines = new List<GameObject>(); // For diver suicide

        _openTiles = new List<Coordinates>();  // Map for feature placement
        _navMap = new List<Node>();            // Map for agent navigation   (Both could be combined but the game is small enough to separate them)

        // Level generation steps
        StartLevelGeneration();
    }



    private void StartLevelGeneration()
    {
        // Reset the cellular automata
        ResetAutomata();

        // Main generation steps
        for (int i = 0; i < _mainSteps; i++)
        {
            Step();
        }

        // Smoothing generation steps
        for (int i = 0; i < _smoothSteps; i++)
        {
            SmoothStep();
        }

        // Chose random coordinates where there isn't a wall
        int[] coords = FindFloodfillCoordinates();

        // Flood fill the area of the selected coordinates with a new value
        FloodFill(coords[0], coords[1], 1, 2); 


        int newPixelCount = 0;

        // Use the flood fill value to replace all other tiles with a wall (effectively filling up unreachable space)
        for (int i = 0; i < _width; ++i) {
            for (int j = 0; j < _height; ++j)
            {
                // Fill walls just to get a closed area
                if(i == 0 || j == 0 || i == _width-1 || j == _height - 1)
                {
                    _caMap[i, j] = 0;
                    continue;
                }

                if (_caMap[i, j] == 2)
                {
                    _caMap[i, j] = 1;
                    newPixelCount++;
                }
                else
                {
                    _caMap[i, j] = 0;
                }
            }
        }

        // Check if the map is filled between 45% and 55% so that the cave isn't too small or too big
        // (Other checks could be done at this point to have a more complex or nicer cave system but for its alrighty for now)
        int percentageFilled = (int)((float)newPixelCount / (float)(_width * _height) * 100);

        //Debug.Log("Map is filled at : "+percentageFilled+"%");
        if(percentageFilled < 45 || percentageFilled > 55)
        {
            StartLevelGeneration();
            return;
        }


        // Create map of open tiles and nodes for features placement and agent navigation respectively
        for (int i = 0; i < _width; ++i) {
            for (int j = 0; j < _height; ++j)
            {
                if (_caMap[i, j] == 1)
                {
                    Node node = new Node(new Coordinates(i, j));
                    node.position = _groundMap.GetCellCenterWorld(new Vector3Int(-node.coords.x + _width / 2, -node.coords.y + _height / 2));
                    _navMap.Add(node);

                    // Add small padding to open coordinates to avoid spawning right next to a wall
                    if (GetNeighbourCellCount(i, j, 1) == 8)
                        _openTiles.Add(new Coordinates(i, j));
                }
            }
        }
        

        // Determine the Diver, Treasure, Mermaid, Shark and Mines spawn locations at the cellular automata's scale
        PlaceFeatures();


        // Render out the map
        UpdateTexture();
    }



    private void PlaceFeatures()
    {
        // Find suitable diver and treasure locations that are far enough to make things interesting
        Coordinates[] coords = FindOriginalCoordinates();

        // Find the mermaid coordinate using the diver and treasure coordinates to spawn at a more interesting location (we can afford this since we use lots of fast recursviness)
        Coordinates mermaidCoords = FindMermaidCoordinates(coords[0], coords[1]);


        // Use simple triangulation to try and place the shark in the middle of the 3 coordinate pairs.
        // If the point is a wall, try to spawn between the diver and the mermaid, if still a wall, try to spawn between diver and treasure, then between mearmaid and treasure and otherwise spawn randomly.
        // A random pathfinding method such a DFS could be used to find the closest open tile but this would take more ressources and the probabilities of the above cases happening are already pretty low so its alrighty.
        // This is to force things to be spiced up since a consistant random spawn could cause the diver and shark to never meet.
        int sharkX = (int)((float)(coords[0].x + coords[1].x + mermaidCoords.x) / 3f);
        int sharkY = (int)((float)(coords[0].y + coords[1].y + mermaidCoords.y) / 3f);

        if (_caMap[sharkX, sharkY] != 1)
        {
            // Try spawn between diver and mermaid
            sharkX = (int)((float)(coords[0].x + mermaidCoords.x) / 2f);
            sharkY = (int)((float)(coords[0].y + mermaidCoords.y) / 2f);
            Debug.Log("Had to change spawn to mermaid");

            if (_caMap[sharkX, sharkY] != 1)
            {
                // Try spawn between diver and treasure
                sharkX = (int)((float)(coords[0].x + coords[1].x) / 2f);
                sharkY = (int)((float)(coords[0].y + coords[1].y) / 2f);
                Debug.Log("Had to change spawn to treasure");

                if (_caMap[sharkX, sharkY] != 1)
                {
                    // Try spawn between mermaid and treasure
                    sharkX = (int)((float)(coords[1].x + mermaidCoords.x) / 2f);
                    sharkY = (int)((float)(coords[1].y + mermaidCoords.y) / 2f);
                    Debug.Log("Had to change spawn to mermaid-treasure");

                    if (_caMap[sharkX, sharkY] != 1)
                    {
                        // Unfortunetly, needs to spawn randomly (rather unlikely)
                        Coordinates randCoords = _openTiles[UnityEngine.Random.Range(0, _openTiles.Count - 1)];
                        sharkX = randCoords.x;
                        sharkY = randCoords.y;
                        Debug.Log("Had to change spawn to random :(");
                    }
                }
            }
        }
        // Don't remove the shark's coordinates from the open map cause the probabilities of an explosive mine spawning on top of it is so small it would actually be hilarious


        // Use Greedy-BFS to draw the shortest path from the diver to the mermaid & from mermaid to treasure
        Vector3[] diverToMermaid = GreedyBFS(coords[0], mermaidCoords).ToArray();
        _mermaidPath = new LinePath(diverToMermaid);

        Vector3[] mermaidToTreasure = GreedyBFS(mermaidCoords, coords[1]).ToArray();
        _treasurePath = new LinePath(mermaidToTreasure);

        Vector3[] mermaidToShark = GreedyBFS(mermaidCoords, new Coordinates(sharkX, sharkY)).ToArray();
        _sharkPath = new LinePath(mermaidToShark);


        // Determine explosive mine coordinates and spawn them based on initial template
        Coordinates mine0 = FindMineCoordinatesFrom(coords[1]);
        Coordinates mine1 = FindMineCoordinatesFrom(coords[1]);
        Coordinates mine2 = FindMineCoordinatesFrom(mine1);
        Coordinates mine3 = FindMineCoordinatesFrom(mine2);
        Coordinates mine4 = FindMineCoordinatesFrom(mermaidCoords);
        Coordinates mine5 = FindMineCoordinatesFrom(mine4);

        SpawnMine(mine0);
        SpawnMine(mine1);
        SpawnMine(mine2);
        SpawnMine(mine3);
        SpawnMine(mine4);
        SpawnMine(mine5);

        // Find closest mine to draw a path from diver to this mine (used to ensure the diver doesn't get stuck looking for a mine)
        Coordinates[] mines = new Coordinates[6] { mine0, mine1, mine2, mine3, mine4, mine5 };
        Coordinates closestMine = mine0;
        float d = Mathf.Infinity;

        foreach (Coordinates mine in mines)
        {
            if (Mathf.Sqrt(Mathf.Pow(mine.x - coords[0].x, 2) + Mathf.Pow(mine.y - coords[0].y, 2)) < d)
            {
                closestMine = mine;
                d = Mathf.Sqrt(Mathf.Pow(mine.x - coords[0].x, 2) + Mathf.Pow(mine.y - coords[0].y, 2));
            }
        }

        Vector3[] diverToMine = GreedyBFS(coords[0], closestMine).ToArray();
        _minePath = new LinePath(diverToMine);


        // Spawn diver by converting tile coordinates
        Vector3 spawnCell = _groundMap.GetCellCenterWorld(new Vector3Int(-coords[0].x + _width / 2, -coords[0].y + _height / 2));
        diver.transform.position = new Vector2(spawnCell.x, spawnCell.y);

        // Spawn treasure 
        Vector3 treasureCell = _groundMap.GetCellCenterWorld(new Vector3Int(-coords[1].x + _width / 2, -coords[1].y + _height / 2));
        treasure.transform.position = new Vector2(treasureCell.x, treasureCell.y);

        // Spawn mermaid 
        Vector3 mermaidCell = _groundMap.GetCellCenterWorld(new Vector3Int(-mermaidCoords.x + _width / 2, -mermaidCoords.y + _height / 2));
        mermaid.transform.position = new Vector2(mermaidCell.x, mermaidCell.y);

        // Spawn shark 
        Vector3 sharkCell = _groundMap.GetCellCenterWorld(new Vector3Int(-sharkX + _width / 2, -sharkY + _height / 2));
        shark.transform.position = new Vector2(sharkCell.x, sharkCell.y);


        // Deactivate the template explosive mine that was used to generate the other ones
        mine.gameObject.SetActive(false);
    }



    /**
     * Spawn an explosive mine at the given coordinates (Can only be done during feature placement since later mines would be deactivated)
     */
    private void SpawnMine(Coordinates spawn)
    {
        Vector3 mineCell = _groundMap.GetCellCenterWorld(new Vector3Int(-spawn.x + _width / 2, -spawn.y + _height / 2));
        GameObject newMine = Instantiate(mine, new Vector2(mineCell.x, mineCell.y), Quaternion.identity, mine.transform.parent);
        newMine.name = "ExplosiveMine";
        _explosiveMines.Add(newMine.gameObject);
    }


    /**
     * Find a suitable distance between 2 points to chose as the diver and treasure location
     */
    private Coordinates[] FindOriginalCoordinates()
    {
        Coordinates coords1 = _openTiles[UnityEngine.Random.Range(0, _openTiles.Count - 1)];
        Coordinates coords2 = _openTiles[UnityEngine.Random.Range(0, _openTiles.Count - 1)];

        // Calculate distance between the 2 coordinates
        float distance = Mathf.Sqrt(Mathf.Pow(coords2.x - coords1.x, 2) + Mathf.Pow(coords2.y - coords1.y, 2));

        // Use loop since recursvieness can cause stack overflow :(
        while(distance < 75)
        {
            coords1 = _openTiles[UnityEngine.Random.Range(0, _openTiles.Count - 1)];
            coords2 = _openTiles[UnityEngine.Random.Range(0, _openTiles.Count - 1)];

            distance = Mathf.Sqrt(Mathf.Pow(coords2.x - coords1.x, 2) + Mathf.Pow(coords2.y - coords1.y, 2));

            if (distance >= 75) break;
        }

        _openTiles.Remove(coords1);
        _openTiles.Remove(coords2);
        return new Coordinates[] { coords1, coords2 };
    }


    /**
     * Find a suitable distance from the diver and treasure to chose as the mermaid spawn location
     */
    private Coordinates FindMermaidCoordinates(Coordinates coords1, Coordinates coords2)
    {
        Coordinates coords = _openTiles[UnityEngine.Random.Range(0, _openTiles.Count - 1)];

        // Calculate distance between the coordinates
        float d1 = Mathf.Sqrt(Mathf.Pow(coords1.x - coords.x, 2) + Mathf.Pow(coords1.y - coords.y, 2));
        float d2 = Mathf.Sqrt(Mathf.Pow(coords2.x - coords.x, 2) + Mathf.Pow(coords2.y - coords.y, 2));

        while (d1 < 45 || d2 < 45)
        {
            coords = _openTiles[UnityEngine.Random.Range(0, _openTiles.Count - 1)];

            d1 = Mathf.Sqrt(Mathf.Pow(coords1.x - coords.x, 2) + Mathf.Pow(coords1.y - coords.y, 2));
            d2 = Mathf.Sqrt(Mathf.Pow(coords2.x - coords.x, 2) + Mathf.Pow(coords2.y - coords.y, 2));

            if (d1 >= 45 && d2 >= 45) break;
        }
        
        _openTiles.Remove(coords);
        return coords;
    }


    /**
     * Find coordinates to spawn explosive mines from a given coordinate
     */
    private Coordinates FindMineCoordinatesFrom(Coordinates coords)
    {
        Coordinates c = _openTiles[UnityEngine.Random.Range(0, _openTiles.Count - 1)];
        float d = Mathf.Sqrt(Mathf.Pow(coords.x - c.x, 2) + Mathf.Pow(coords.y - c.y, 2));

        while (d < 15 || d > 20)
        {
            c = _openTiles[UnityEngine.Random.Range(0, _openTiles.Count - 1)];

            d = Mathf.Sqrt(Mathf.Pow(coords.x - c.x, 2) + Mathf.Pow(coords.y - c.y, 2));

            if (d >= 15 && d <= 20) break;
        }

        _openTiles.Remove(c);
        return c;
    }


    /**
     * Find some random and suitable coordinates for flood fill (used to be for feature placement too but could cause some stack overflows)
     */
    private int[] FindFloodfillCoordinates()
    {
        int randX = UnityEngine.Random.Range(0, _width - 1);
        int randY = UnityEngine.Random.Range(0, _height - 1);

        if (_caMap[randX, randY] == 1)
            return new int[] { randX, randY };
        else
            return FindFloodfillCoordinates();
    }


    /**
     * Flood fill the area from a given (x,y) position with the target value if it is of the given source value
     */
    private void FloodFill(int x, int y, int source, int target)
    {
        // Check for out of bounds
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return;

        // Check if the current pixel is not equal to the source color
        if (_caMap[x, y] != source)
            return;

        _caMap[x, y] = target;

        // Recursively fill the surrounding pixels
        FloodFill(x - 1, y, source, target); // left
        FloodFill(x + 1, y, source, target); // right
        FloodFill(x, y + 1, source, target); // top
        FloodFill(x, y - 1, source, target); // bottom
    }


    /**
     * Reset the cellular automata and fill back using the start fill value
     */
    private void ResetAutomata()
    {
        _caMap = new int[_width, _height];
        _openTiles.Clear();

        for (int x = 0; x < _width; ++x) {
            for (int y = 0; y < _height; ++y)
            {
                _caMap[x, y] = UnityEngine.Random.value > _startFill ? 0 : 1;
            }
        }
    }


    /**
     * Update the tilemaps with the corresponding ground or wall tiles
     */
    void UpdateTexture()
    {

        Color color = Color.white;

        for (int x = 0; x < _width; x++) {
            for (int y = 0; y < _height; y++)
            {
                var value = _caMap[x, y];

                switch (value)
                {
                    case 0:
                        _wallMap.SetTile(new Vector3Int(-x + _width / 2, -y + _height / 2), wallTile);
                        _groundMap.SetTile(new Vector3Int(-x + _width / 2, -y + _height / 2), null);
                        break;
                    case 1:
                        _groundMap.SetTile(new Vector3Int(-x + _width / 2, -y + _height / 2), groundTile);
                        _wallMap.SetTile(new Vector3Int(-x + _width / 2, -y + _height / 2), null);
                        break;

                    default:
                        _groundMap.SetTile(new Vector3Int(-x + _width / 2, -y + _height / 2), groundTile);
                        _wallMap.SetTile(new Vector3Int(-x + _width / 2, -y + _height / 2), null);
                        break;
                }
            }
        }
    }


    /**
     * Get the number of neihgbours in a particular radius around an (x,y) position
     */
    private int GetNeighbourCellCount(int x, int y, int radius)
    {
        int neighbourCellCount = 0;

        if (x > radius - 1)
        {
            neighbourCellCount += _caMap[x - radius, y];
            if (y > radius - 1)
                neighbourCellCount += _caMap[x - 1, y - 1];
        }

        if (y > radius - 1)
        {
            neighbourCellCount += _caMap[x, y - radius];
            if (x < _width - radius)
                neighbourCellCount += _caMap[x + radius, y - radius];
        }

        if (x < _width - radius)
        {
            neighbourCellCount += _caMap[x + radius, y];
            if (y < _height - radius)
                neighbourCellCount += _caMap[x + radius, y + radius];
        }

        if (y < _height - radius)
        {
            neighbourCellCount += _caMap[x, y + radius];
            if (x > radius - 1)
                neighbourCellCount += _caMap[x - radius, y + radius];
        }

        return neighbourCellCount;
    }

    /**
     * Main cellular automata step function using the neighbours count in the first and second rings. 
     */
    public void Step()
    {
        for (int x = 0; x < _width; ++x) {
            for (int y = 0; y < _height; ++y)
            {
                int R1 = _caMap[x, y] + GetNeighbourCellCount(x, y, 1);
                int R2 = _caMap[x, y] + GetNeighbourCellCount(x, y, 2);

                _caMap[x, y] = (R1 > _r1Limit || R2 < _r2Limit) ? 1 : 0;
            }
        }

        // Used for debug button
        //UpdateTexture();
    }

    /**
     * Smooth cellular automata step function using the R1 neighbours to smooth the level out. 
     */
    public void SmoothStep()
    {
        for (int x = 0; x < _width; ++x) {
            for (int y = 0; y < _height; ++y)
            {
                int R1 = _caMap[x, y] + GetNeighbourCellCount(x, y, 1);
                _caMap[x, y] = R1 > _r1Limit ? 1 : 0;

            }
        }

        // Used for debug button
        //UpdateTexture();
    }


    /**
     * Greedy Best First Search algorithm. Adapted for this project but heavily inspired from: https://github.com/dbrizov/Unity-PathFindingAlgorithms/blob/master/Assets/Scripts
     * Big thanks for his MinHeap class acting like the required PriorityQueue() that .net is missing
     * A* was tested but, as expected, the results are the same as Greedy-BFS since each node has the same cost
     */
    private List<Vector3> GreedyBFS(Coordinates start, Coordinates goal)
    {
        // Reset each node's heuristic, cost, and parent
        foreach (Node node in _navMap)
        {
            node.heuristic = Mathf.Infinity;
            node.parent = null;
        }

        // Find start and goal nodes
        Node startNode = GetNodeByCoords(start);
        Node endNode = GetNodeByCoords(goal);

        // Heuristic comparison to get lowest heuristic for MinHeap
        Comparison<Node> heuristicComparison = (lhs, rhs) =>
        {
            float lCost = heuristic(lhs.coords, goal);
            float rCost = heuristic(rhs.coords, goal);

            return lCost.CompareTo(rCost);
        };

        // Create frontier queue and visited set
        MinHeap<Node> openList = new MinHeap<Node>(heuristicComparison);
        openList.Add(startNode);

        HashSet<Node> closedList = new HashSet<Node>();
        closedList.Add(startNode);

        // Main search loop
        while(openList.Count > 0)
        {
            // Pop node with lowest cost+heuristic
            Node currentNode = openList.Remove();
            
            // Goal found - Retrace path using parent nodes to return the List<Vector3>
            if (AreNoodsSame(currentNode, endNode))
                return TracePath(startNode, currentNode);
            
            // Loop children and add to frontier when needed
            foreach (Node child in GetChildNodes(currentNode))
            {
                // Add to frontier
                if (!closedList.Contains(child))
                {
                    openList.Add(child);
                    closedList.Add(child);
                    child.parent = currentNode;
                }
            }
        }

        return null;
    }

    private bool AreNoodsSame(Node node1, Node node2)
    {
        return (node1.coords.x == node2.coords.x && node1.coords.y == node2.coords.y);
    }

    private Node GetNodeByCoords(Coordinates coords)
    {
        foreach(Node node in _navMap)
        {
            if (node.coords.x == coords.x && node.coords.y == coords.y)
                return node;
        }

        return null;
    }

    /**
     * Trace the path from a given node back to its origin
     */
    private List<Vector3> TracePath(Node start, Node end)
    {
        List<Vector3> path = new List<Vector3>();

        path.Add(end.position);
        Node current = end.parent;

        while(current != null)
        {
            path.Add(current.position);
            current = current.parent;
        }

        path.Reverse();

        return path;
    }

    private List<Node> GetChildNodes(Node node)
    {
        List<Node> childNodes = new List<Node>();

        for(int dx = node.coords.x - 1; dx <= node.coords.x + 1; dx++) {
            for(int dy = node.coords.y - 1; dy <= node.coords.y + 1; dy++)
            {
                // Skip out of bound coordinates
                if (dx < 0 || dx > _width - 1 || dy < 0 || dy > _height - 1) continue;

                // Skip if the origin cell or walls
                if ((dx == node.coords.x && dy == node.coords.y) || (_caMap[dx, dy] != 1)) continue;

                Coordinates childCoord = new Coordinates(dx, dy);
                childNodes.Add(GetNodeByCoords(childCoord));
            }
        }

        return childNodes;
    }

    /**
     * Heuristic function based on euclidean distance which is enough given the 2D environment 
     */
    private float heuristic(Coordinates current, Coordinates goal)
    {
        return Mathf.Sqrt(Mathf.Pow(goal.x - current.x, 2) + Mathf.Pow(goal.y - current.y, 2));
    }
}



// Used for A* algorithm
class Node
{
    public Coordinates coords;
    public Vector3 position;

    public Node parent;
    public float heuristic;

    public Node(Coordinates coords)
    {

        this.coords = coords;
        this.parent = null;
        this.heuristic = Mathf.Infinity;
    }
}



// Just because it's easier to process them like this
class Coordinates {
    public int x, y;
    public Coordinates(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
}