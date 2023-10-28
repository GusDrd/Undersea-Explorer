# Undersea-Explorer

[![Unity](https://img.shields.io/badge/unity_2021.3.17f1-%23000000.svg?style=for-the-badge&logo=unity&logoColor=white)](https://unity.com/)

This small 2D unity project aimed to explore procedural generation via cellular, agent behaviour management, and path finding behaviours/algorithms.

## Content
### Generator
All of the level generation happens inside the ```Assets/Generator/LevelGenerator.cs``` script. \
It not only generates the levels but also places features (mines, chest) and agents (diver, mermaid, shark) for the game. \
It uses cellular automata as a base to create the level outline and then uses a flood fill algorithm to create one single network of caves which also avoids having unaccessible areas.

Once a base level has been created, the level's area is computed and if it is either too small or too big, a new map is generated. This is done to avoid having a level too small for the agents to play or a map too big that doesn't look like a cave network anymore.

Finally, the different features and agents are placed inside the level, ready to play.

### Agents
The agents' behaviours are pretty straight forward and dictate how the game will go. If the diver dies, its a loss and if the diver gets to the treasure chest, it's a win.

The diver is the most complex agent. Its main goals will be to first get to the mermaid and then get to the treasure chest. However, if for some reason the mermaid dies, then its only
goal will be to die on an explosive mine.

Other behaviour explanations can be found directly inside the agents' files found in the ```Assets/Agents``` folder.

## Credits
Thanks to [Bronson Zgeb](https://bronsonzgeb.com/index.php/2022/01/30/procedural-generation-with-cellular-automata/) and [Roguebasin](http://www.roguebasin.com/index.php/Cellular_Automata_Method_for_Generating_Random_Cave-Like_Levels#C.23_Code) for their help with the cellular automata's base implementation.

The Greedy Best First Search algorithm adapted for this project was heavily inspired from [Denis Rizov](https://github.com/dbrizov/Unity-PathFindingAlgorithms/blob/master/Assets/Scripts). \
Big thanks for his MinHeap class acting like the necessary PriorityQueue() that .NET is missing.

To manage the agent behaviours, the [NPBehave](https://github.com/meniku/NPBehave) library was used and to manage the agents' reactive navigation, part of the [Movement AI](https://github.com/sturdyspoon/unity-movement-ai) library was used. \
The specific scripts used can be found in the ```Assets/Agents/Movement``` folder.
