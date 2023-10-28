using NPBehave;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityMovementAI;
using static UnityEngine.GraphicsBuffer;

public class Mermaid : MonoBehaviour
{
    public GameObject diver;
    public GameObject shark;

    private SteeringBasics steeringBasics;
    private WallAvoidance wallAvoidance;
    private FollowPath followPath;
    private Rigidbody2D rb;
    private Wander wander;

    private bool _foundDiver = false;
    private bool _diverChaseSpeed = false;

    private LinePath sharkPath;


    // NPBehave tree
    private Root tree;

    private const int WANDER = 0;  // WANDER Action
    private const int SEEK = 1;    // SEEK Action
    private const int FOLLOW = 2;  // FOLLOW Action
    private const int KILL = 3;    // KILL Action

    private int currentAction;        // Current action
    private List<int> utilityScores;  // Each action's utility score

    private bool isAlive = true;


    void Start()
    {
        steeringBasics = GetComponent<SteeringBasics>();
        wallAvoidance = GetComponent<WallAvoidance>();
        followPath = GetComponent<FollowPath>();
        rb = GetComponent<Rigidbody2D>();
        wander = GetComponent<Wander>();

        sharkPath = LevelGenerator._sharkPath;

        // Set initial action
        currentAction = WANDER;
        SwitchTree(SelectBehaviourTree(currentAction));

        // Set utility scores to zero
        utilityScores = new List<int>();
        utilityScores.Add(0);  // Wander
        utilityScores.Add(0);  // Seek
        utilityScores.Add(0);  // Follow
        utilityScores.Add(0);  // Kill
    }


    void Update()
    {
        if(isAlive)
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
    }


    /**
     * Update the behaviours' utility scores
     */
    private void updateScores()
    {
        // Controls diver "distress level with a cap of 100 and increasing faster than how it depleats
        if (shark.gameObject != null && shark.GetComponent<Shark>()._chasingDiver)
        {
            if (!_diverChaseSpeed)
            {
                steeringBasics.maxVelocity = steeringBasics.maxVelocity + 25;
                _diverChaseSpeed = true;
            }

            if(utilityScores[KILL] < 100)
                utilityScores[KILL] += 2;
        }else
        {
            if (_diverChaseSpeed)
            {
                steeringBasics.maxVelocity = steeringBasics.maxVelocity - 25;
                _diverChaseSpeed = false;
            }

            if(utilityScores[KILL] > 0)
                utilityScores[KILL] -= 1;
        }

        if (Vector3.Distance(transform.position, diver.transform.position) < 60)
        {
            utilityScores[WANDER] = 0;
            utilityScores[SEEK] = 1;
        }
        else
        {
            utilityScores[WANDER] = 1;
            utilityScores[SEEK] = 0;
        }

        if (_foundDiver)
            utilityScores[FOLLOW] = 2;
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
            case WANDER:
                return new Root(new Action(() => Wander()));

            case SEEK:
                return new Root(new Action(() => SeekDiver()));

            case FOLLOW:
                return new Root(new Action(() => Follow()));

            case KILL:
                return new Root(new Action(() => SeekShark()));

            default:
                return new Root(new Action(() => Wander()));
        }
    }


    /**
     * Wander around spawn point behaviour
     */
    private void Wander()
    {
        Vector3 accel = wander.GetSteering();

        steeringBasics.Steer(accel);
    }

    /**
     * Seek shark to kill it behaviour
     */
    private void SeekShark()
    {
        if (sharkPath == null || shark.gameObject == null)
            return;

        Vector3 accel = steeringBasics.Seek(shark.transform.position);

        if (rb.velocity.magnitude < .5f || Vector3.Distance(transform.position, diver.transform.position) > 100)
            accel = followPath.GetSteering(sharkPath);

        steeringBasics.Steer(accel);

        sharkPath.Draw();
    }

    /**
     * Seek diver behaviour
     */
    private void SeekDiver()
    {
        Vector3 accel = wallAvoidance.GetSteering();

        if (accel.magnitude < 0.005f)
            accel = steeringBasics.Seek(diver.transform.position);

        steeringBasics.Steer(accel);
    }

    /**
     * Follow and wander arround the diver
     */
    private void Follow()
    {
        Vector3 accel = steeringBasics.Seek(diver.transform.position);

        if (accel.magnitude < .05f || Vector3.Distance(transform.position, diver.transform.position) < 5)
            accel = wander.GetSteering();


        steeringBasics.Steer(accel);
    }



    /**
     * Check for collisions with other entities (diver, shark & mine)
     */
    void OnCollisionEnter2D(Collision2D collider)
    {
        // Mermaid collided with an explosive mine - she dies D:
        if (collider.gameObject.name == "ExplosiveMine")
        {
            Debug.Log("Mermaid died! This makes someone pretty sad...");

            LevelGenerator._explosiveMines.Remove(collider.gameObject);

            diver.GetComponent<Diver>()._isMermaidAlive = false;
            diver.GetComponent<Diver>().firstClosestMine = null;
            isAlive = false;
            tree.Stop();

            Destroy(gameObject);
            Destroy(collider.gameObject);
        }


        // Mermaid united with the diver
        if (collider.gameObject.name == "Diver" && !_foundDiver)
            _foundDiver = true;
    }
}
