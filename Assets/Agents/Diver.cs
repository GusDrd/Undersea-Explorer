using NPBehave;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityMovementAI;
using static UnityEngine.GraphicsBuffer;

public class Diver : MonoBehaviour
{
    // Public variables
    public GameObject treasure;
    public GameObject mermaid;
    public GameObject shark;

    public bool isAi;
    public float controlSpeed;
    public int sharkEscape;


    // Navigation paths
    private LinePath mermaidPath;
    private LinePath treasurePath;

    private GameObject closestMine;
    [HideInInspector] public GameObject firstClosestMine;

    // Components
    private SteeringBasics steeringBasics;
    private WallAvoidance wallAvoidance;
    private FollowPath followPath;
    private Rigidbody2D rb;
    private Wander wander;
    private Flee flee;

    // Controler
    private float vertical, horizontal;

    // Behaviours variables
    [HideInInspector] public bool _hasMetMermaid = false;
    [HideInInspector] public bool _isMermaidAlive = true;

    private bool _diverChaseSpeed = false;
    private Vector3 mermaidSpawnPos;

    // NPBehave tree
    private Root tree;

    private const int GO_MERMAID = 0;     // FOLLOW PATH Action
    private const int GO_TREASURE = 1;    // FOLLOW PATH Action
    private const int FIND_MERMAID = 2;   // SEEK MERMAID Action
    private const int WANDER_MERMAID = 3; // WANDER MERMAID Action
    private const int FLEE_SHARK = 4;     // FLEE SHARK Action
    private const int AVOID_MINE = 5;     // AVOID MINE Action
    private const int SUICIDE = 6;        // SUICIDE Action

    private int currentAction;        // Current action
    private List<int> utilityScores;  // Each action's utility score



    void Start()
    {
        steeringBasics = GetComponent<SteeringBasics>();
        wallAvoidance = GetComponent<WallAvoidance>();
        followPath = GetComponent<FollowPath>();
        rb = GetComponent<Rigidbody2D>();
        wander = GetComponent<Wander>();
        flee = GetComponent<Flee>();

        mermaidPath = LevelGenerator._mermaidPath;
        treasurePath = LevelGenerator._treasurePath;

        flee.panicDist = sharkEscape;

        mermaidSpawnPos = mermaid.transform.position;

        // Set initial action
        currentAction = GO_MERMAID;
        SwitchTree(SelectBehaviourTree(currentAction));

        // Set utility scores to zero
        utilityScores = new List<int>();
        utilityScores.Add(0);  // Follow mermaid path
        utilityScores.Add(0);  // Follow treasure path
        utilityScores.Add(0);  // Wander around
        utilityScores.Add(0);  // Seek mermaid
        utilityScores.Add(0);  // Flee shark
        utilityScores.Add(0);  // Avoid mines
        utilityScores.Add(-1); // Suicide

        StartCoroutine(UpdateClosestMine());
    }


    void Update()
    {
        // Agent control the diver
        if (isAi)
        {
            updateScores();

            int maxValue = utilityScores.Max(t => t);
            int maxIndex = utilityScores.IndexOf(maxValue);

            if (currentAction != maxIndex)
            {
                currentAction = maxIndex;
                SwitchTree(SelectBehaviourTree(currentAction));
            }
        }
        else
        {
            // Manually control the diver
            horizontal = Input.GetAxis("Horizontal");
            vertical = Input.GetAxis("Vertical");

            rb.velocity = new Vector2(horizontal * controlSpeed, vertical * controlSpeed);
        }
    }


    /**
     * Check for collisions with objects (treasure)
     */
    void OnTriggerEnter2D(Collider2D collider)
    {
        // Diver collides with treasure - it's a win! (Also check that the diver has met the mermaid but should be the case at this point)
        if (collider.gameObject.name == "Treasure" && _hasMetMermaid)
        {
            Debug.Log("Treasure collected! 2/2 WIN");
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }

    /**
     * Check for collisions with other entities (mermaid, shark & mines)
     */
    void OnCollisionEnter2D(Collision2D collider)
    {
        // Diver collided with an explosive mine - it's a lose
        if (collider.gameObject.name == "ExplosiveMine")
        {
            Debug.Log("Colliding with explosive mine! LOSE");
            UnityEditor.EditorApplication.isPlaying = false;
        }

        // Diver collided with the shakr - it's a lose
        if (collider.gameObject.name == "Shark")
        {
            Debug.Log("Diver has been eaten by the shark! LOSE");
            UnityEditor.EditorApplication.isPlaying = false;
        }

        // Diver collided with the mermaid, they are united!
        if (collider.gameObject.name == "Mermaid" && !_hasMetMermaid)
        {
            Debug.Log("Diver has united with the mermaid! 1/2");
            _hasMetMermaid = true;
        }
    }


    /**
     * Update the behaviours' utility scores
     */
    private void updateScores()
    {
        // Check if diver is in distance of being chased by the shark (MEDIUM-bis priority)
        if(shark.gameObject != null && Vector3.Distance(transform.position, shark.transform.position) < sharkEscape)
        {
            if (!_diverChaseSpeed)
            {
                steeringBasics.maxVelocity = steeringBasics.maxVelocity + 10;
                _diverChaseSpeed = true;
            }

            utilityScores[FLEE_SHARK] = 5;
            utilityScores[GO_MERMAID] = 0;
            utilityScores[GO_TREASURE] = 0;
        }else
        {
            if (_diverChaseSpeed)
            {
                steeringBasics.maxVelocity = steeringBasics.maxVelocity - 10;
                _diverChaseSpeed = false;
            }

            utilityScores[FLEE_SHARK] = 0;
        }

        // Check if diver is close enough to mermaid to seek her (LOW priority)
        if (mermaid.gameObject != null && Vector3.Distance(transform.position, mermaid.transform.position) < 60)
            utilityScores[FIND_MERMAID] = 3;
        else
            utilityScores[FIND_MERMAID] = 0;


        // Check if mermaid origin has been reached
        if(Vector3.Distance(transform.position, mermaidSpawnPos) < 5)
            utilityScores[WANDER_MERMAID] = 2;
        else
            utilityScores[WANDER_MERMAID] = 0;


        // Check for which path the diver should follow (LOWEST + MEDIUM priority)
        if (_hasMetMermaid)
        {
            utilityScores[GO_MERMAID] = 0;
            utilityScores[GO_TREASURE] = 4;
        }
        else
        {
            utilityScores[GO_MERMAID] = 1;
            utilityScores[GO_TREASURE] = 0;
        }

        // Check if diver is about to collide with a mine (HIGH priority)
        if (closestMine != null && Vector3.Distance(transform.position, closestMine.transform.position) <= 15f)
        {
            utilityScores[AVOID_MINE] = 50;
        }
        else
            utilityScores[AVOID_MINE] = 0;

        // Check if the mermaid is alive (HIGHEST priority)
        if (!_isMermaidAlive)
            utilityScores[SUICIDE] = 100;
    }


    /**
     * Switch between and Select the different behaviours
     */
    private void SwitchTree(Root t)
    {
        if (tree != null) tree.Stop();

        tree = t;
        tree.Start();
    }

    private Root SelectBehaviourTree(int action)
    {
        switch (action)
        {
            case GO_MERMAID:
                return new Root(new Action(() => FollowPath(mermaidPath)));

            case GO_TREASURE:
                return new Root(new Action(() => FollowPath(treasurePath)));

            case WANDER_MERMAID:
                return new Root(new Action(() => WanderMermaid()));

            case FIND_MERMAID:
                return new Root(new Action(() => FindMermaid()));

            case FLEE_SHARK:
                return new Root(new Action(() => FleeShark()));

            case AVOID_MINE:
                return new Root(new Action(() => AvoidMine()));

            case SUICIDE:
                return new Root(new Action(() => PursueMine()));

            default:
                return new Root(new Action(() => FollowPath(mermaidPath)));
        }
    }


    /**
     * Follow a given path behaviour
     */
    public void FollowPath(LinePath path)
    {
        if (path == null)
            return;


        Vector3 accel = wallAvoidance.GetSteering();

        if (accel.magnitude < 0.005f)
            accel = followPath.GetSteering(path);

        steeringBasics.Steer(accel);

        path.Draw();
    }

    /**
     * Flee the shark behaviour
     */
    public void FleeShark()
    {
        Vector3 accel = wallAvoidance.GetSteering();

        if (accel.magnitude < 0.005f)
            accel = flee.GetSteering(shark.transform.position);

        steeringBasics.Steer(accel);
    }

    /**
     * Pursue an explosive mine behaviour
     */
    public void PursueMine()
    {
        Vector3 accel = wallAvoidance.GetSteering();

        // If the first closest mine has been detonated, seek other mine
        if (firstClosestMine == null && closestMine != null && accel.magnitude < 0.005f)
            accel = steeringBasics.Seek(closestMine.transform.position);
        
        // Follow path to closest mine from spawn
        if (accel.magnitude < 0.005f)
            accel = followPath.GetSteering(LevelGenerator._minePath);

        steeringBasics.Steer(accel);
    }

    /**
     * Avoid an explosive mine behaviour
     */
    public void AvoidMine()
    {
        Vector3 accel = flee.GetSteering(closestMine.transform.position);

        steeringBasics.Steer(accel);
    }

    /**
     * Seek the mermaid if she's been seen
     */
    public void FindMermaid()
    {
        if (rb.velocity.magnitude == 0)
            FollowPath(mermaidPath);

        Vector3 accel = wallAvoidance.GetSteering();

        if (accel.magnitude < 0.005f)
            accel = steeringBasics.Seek(mermaid.transform.position);

        steeringBasics.Steer(accel);
    }

    /**
     * Wander around where the mermaid was last seen
     */
    public void WanderMermaid()
    {
        Vector3 accel = wander.GetSteering();

        steeringBasics.Steer(accel);
    }



    /**
     * Find the closest explosive mine to the diver's current position
     */
    private IEnumerator UpdateClosestMine()
    {
        float distance = Mathf.Infinity;

        // Update every second
        while (true)
        {
            if(closestMine != null)
                distance = Vector3.Distance(transform.position, closestMine.transform.position); ;

            foreach (GameObject mine in LevelGenerator._explosiveMines)
            {
                if (Vector3.Distance(transform.position, mine.transform.position) < distance && closestMine != mine)
                {
                    closestMine = mine;
                    distance = Vector3.Distance(transform.position, mine.transform.position);
                }
            }

            // Remember the first closest mine in case mermaid dies on it
            if (firstClosestMine == null)
                firstClosestMine = closestMine;

            yield return new WaitForSeconds(.5f);
        }
    }
}
