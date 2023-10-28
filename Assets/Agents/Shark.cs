using NPBehave;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityMovementAI;

public class Shark : MonoBehaviour
{

    public GameObject diver;

    public int sharkEscape;


    private SteeringBasics steeringBasics;
    private WallAvoidance wallAvoidance;
    private Wander wander;

    private bool isAlive = true;
    [HideInInspector] public bool _chasingDiver = false;

    // NPBehave tree
    private Root tree;

    private const int WANDER = 0;  // WANDER Action
    private const int SEEK = 1;    // SEEK Action

    private int currentAction;        // Current action
    private List<int> utilityScores;  // Each action's utility score


    void Start()
    {
        steeringBasics = GetComponent<SteeringBasics>();
        wallAvoidance = GetComponent<WallAvoidance>();
        wander = GetComponent<Wander>();

        // Set initial action
        currentAction = WANDER;
        SwitchTree(SelectBehaviourTree(currentAction));

        // Set utility scores to zero
        utilityScores = new List<int>();
        utilityScores.Add(0);  // Wander
        utilityScores.Add(0);  // Seek
    }


    void Update()
    {
        if (isAlive)
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
        if (Vector3.Distance(transform.position, diver.transform.position) < sharkEscape - 10)
        {
            utilityScores[WANDER] = 0;
            utilityScores[SEEK] = 1;
            _chasingDiver = true;
        }
        else
        {
            utilityScores[WANDER] = 1;
            utilityScores[SEEK] = 0;
            _chasingDiver = false;
        }
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
                return new Root(new Action(() => Seek(diver.transform.position)));

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
     * Seek for diver behaviour
     */
    private void Seek(Vector3 pos)
    {
        Vector3 accel = wallAvoidance.GetSteering();

        if (accel.magnitude < 0.005f)
            accel = steeringBasics.Seek(diver.transform.position);

        steeringBasics.Steer(accel);
    }

    /**
     * Check for collisions with other entities (diver, or mermaid, the shark can actually play with mines)
     */
    void OnCollisionEnter2D(Collision2D collider)
    {
        // Mermaid killed shark
        if (collider.gameObject.name == "Mermaid")
        {
            Debug.Log("Mermaid killed the shark!");
            isAlive = false;
            tree.Stop();
            Destroy(gameObject);
        }
    }
}
