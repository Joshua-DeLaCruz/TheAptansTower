# Game Brief – Concept Version

This file describes the game I am building for my minor (High Tech AI-Systems, Fontys). Read it fully before you start. Your task is to build a **playable concept version** of the game in this Unity project.

## How to work on this brief

The project rules and the subagent workflow are in `CLAUDE.md`. For this brief specifically:

- Before writing code, inspect the project (Unity version, installed packages, existing folders and scripts) and tell me what you found.
- Work one milestone at a time, split into small tasks. Propose the plan for each milestone and wait for my OK.
- After each milestone, stop and tell me exactly what to do in Unity to test it.
- If something is unclear, ask me instead of guessing.

## The game in short

A 2D side-view action platformer with a retro, medieval look. The player explores a small world, gathers materials to get stronger, and then climbs a tower of bosses. The bosses adapt to the player's skill, and each boss passes what it learned about the player on to the next boss. The goal is to keep every player in "flow": not so hard that they get frustrated, not so easy that they get bored.

## The world (left to right)

1. **Town** – the starting point. A shop where the player can buy armour and sell materials.
2. **Plains** – small monsters such as slimes and birds.
3. **Forest** – bigger monsters such as bears and wolves. The player can chop trees for wood and mine rocks for ores.
4. **Tower entrance** – the first boss arena. Beating the boss opens the stairs to the next floor.

The core loop: explore → fight → gather → sell and buy armour in town → get stronger → face the boss.

## The tower

- Each floor has its own theme and boss, for example desert, underworld, sea world, medieval and toxic.
- The concept version needs only **two floors** to prove the idea. The structure must make it easy to add more floors later.

## Adaptive bosses (the most important part)

During every boss fight, the game tracks three player signals:

1. **Hits taken** – how often the player gets hit.
2. **Reaction speed** – how fast the player dodges or responds after a boss attack starts.
3. **Attack preferences** – which player attacks are used most.

These signals are stored in a **player profile** that is saved after each fight and passed on to the next boss. The next boss starts the fight already adjusted to that profile.

Later in the minor, the boss behaviour will be trained with **reinforcement learning using Unity ML-Agents**. For the concept version:

- Build the boss with a simple **rule-based brain** that reads the player profile and adjusts things like attack speed, attack choice and aggression.
- Put the brain behind an interface (for example `IBossBrain`), so I can later swap it for an ML-Agents brain without rewriting the boss.
- Do not install or set up ML-Agents yet unless I ask.

## Concept version milestones

**M1 – Player**
Movement, jumping, a dodge, a basic melee attack plus at least one second attack type (so attack preferences can be tracked), health and damage.

Decisions for M1:
- The second attack is a **thrown dagger**. Daggers are limited: the player buys them in the shop (M2). Until then the player starts with a small amount set in the Inspector. The dagger count lives behind its own small interface, so it can later be connected to the inventory and shop.
- Input is defined in code (Input System `InputAction`s with default keyboard and gamepad bindings), not in an `.inputactions` asset.
- David writes the `Health` logic himself from an outline with TODO comments; Claude reviews it afterwards. The `IDamageable` interface (`bool TakeDamage(int amount, GameObject source)`) and a `TrainingDummy` test tool are provided.
- The dodge only works on the ground. Every jump gets a short safe window at take-off (adjustable, 0.1 s by default). Jumping during a dodge cancels it into a running jump in the dodge's direction (even with no direction held), and the jump's window replaces the rest of the dodge's window. The jump keeps part of the dodge's speed (Inspector slider, 0.3 by default), which fades out during the jump; at most it lands about 1.1 units further than a normal running jump. Holding the opposite direction at the cancel gives a normal jump back.
- `PlayerInvulnerability` is the single place that answers "can the player be hit right now?". The dodge and the jump open windows on it, and `Health` only asks this component.

**M2 – World**
Town, plains and forest in one scene or connected scenes. A few monster types with simple behaviour. Chopping trees and mining rocks. A simple inventory and a shop in town (sell materials, buy armour that reduces damage).

**M3 – First boss and player tracking**
A boss arena with a boss that has two or three attacks. A tracker that measures the three player signals during the fight. A player profile that is saved (JSON is fine) after the fight.

**M4 – Tower and adaptation**
Stairs to a second floor with a different theme and a second boss. The second boss loads the player profile and adapts to it through the rule-based brain. Show the profile values on screen in a small debug panel, so I can see the adaptation working.

## Technical notes

- Unity 6 with C#. Use the project's existing packages; tell me before adding new ones.
- Never create or edit `.meta`, `.unity` or `.prefab` files by hand. Where scenes, prefabs or objects need to be set up, either write an **Editor script** (for example a menu item `Tools/Build Concept Scene`) that I run myself, or give me clear step-by-step instructions.
- Use simple placeholder art (coloured shapes) for now. David makes the real medieval art himself later.
- Art is planned at **32 pixels per unit**. Visuals (sprites, renderers) always sit on a separate child object (for example `Visual`), never on the object that holds the logic and physics, so sprites can be swapped without changing code.
- Put scripts under `Assets/_Project/Scripts/`, in folders such as `Player`, `Enemies`, `Boss`, `World`, `Data` and `ML`.
- Keep this file up to date if we agree on changes to the plan.
