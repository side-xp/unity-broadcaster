# Broadcaster Demo

The packages comes with a sample project that illustrates how to use the Broadcaster features in real situations.

You can import it from the *Packages Manager* window:

1. In your *Unity* project, go to `Window > Package Management > Package Manager`
2. Select *SideXP - Broadcaster* in the packages list
3. Click on the *Samples* tab
4. Import the *Scavengers* project

This will install the sample files into your `Assets` folder.

## The game concept

The game is a turn-based tile-bassed roguelike where the player must survive to incoming zombies.

In short:

- The player can move by pressing arrow keys
- Each movement is a turn and 1 Food
- Running out of food is game over
- Enemies can also move after player turn, and attack the player if in range
- Enemy hits decrease food
- Move on a tile with food grants +10 food, soda grats +20 food
- Enemies can't be defeated, and will increase in number as the player progresses
- Reach the *Exit* panel to go to the next level

The concept come from an [old Unity tutorial](https://learn.unity.com/course/intermediate-3d-game-development/unit/2d-roguelike-tutorial-legacy). We reused their scripts and assets, but reworked it with Broadcaster features.

## @todo

@todo Explain how the project was at its initial state, and how we replaced the code with Broadcaster features to fix the issues