# The Aptans Tower – instructions for Claude Code

## About the project
The Aptans Tower is a 2D side-view action platformer with a retro, medieval look and hard boss fights inspired by souls games. The player explores a small world (town, plains, forest), fights monsters, gathers materials and buys armour, and then climbs a tower of themed floors. Each floor has a boss that adapts to the player's skill, and each boss passes what it learned about the player on to the next boss.

The adaptive bosses look at three player signals: how often the player gets hit, how fast the player reacts, and which attacks the player prefers. These signals are stored in a player profile that is saved after each fight and loaded by the next boss. In the concept version, the bosses use a rule-based brain. Later they will use reinforcement learning (Unity ML-Agents), through the same brain interface.

The full game design and the current milestones are in `GAME_BRIEF.md`. Read it before starting work on a new feature.

This is an individual study for the Fontys minor High Tech AI-Systems (Sep 2026 – Jan 2027). The developer is David. He knows C#, PHP and Unity basics (he built a 2D platformer before), but he is new to machine learning. Part of his study is evaluating how efficient AI coding agents are and when they help or hurt his learning. So the way we work together matters as much as the code.

## Tech setup
- Unity 6 LTS (6000.x), Universal 2D template (URP with 2D lights)
- C# for all game code
- Unity ML-Agents for the bosses (package `com.unity.ml-agents`, training with the `mlagents` Python package)
- Unity Input System (new input system, not the old `Input.GetKey`)
- Git and GitHub for version control

Unity 6 notes: use `Rigidbody2D.linearVelocity` (not `velocity`), and `FindFirstObjectByType` / `FindAnyObjectByType` (not `FindObjectOfType`).

## Folder structure
All our own work goes under `Assets/_Project/`:
- `Scripts/Player` – movement, combat, health
- `Scripts/Enemies` – monsters in the world (slimes, birds, bears, wolves)
- `Scripts/World` – town, shop, inventory, armour, gathering (trees, ores)
- `Scripts/Tower` – tower floors, floor themes, moving between floors
- `Scripts/Boss` – boss body: attacks, states, health
- `Scripts/ML` – boss brains (rule-based and ML-Agents), the player-signal tracker, the player profile, simulated players
- `Scripts/Arena` – level, camera, hazards
- `Scripts/Core` – shared code (game manager, events, saving, utilities)
- `Data` – ScriptableObjects for items, armour, monster stats and floor themes
- `Prefabs`, `Scenes`, `Art`, `Audio`
- Training configs go in `ml-config/` at the repository root (not inside Assets).
- Training results go in `results/` at the root, which is ignored by Git (except final `.onnx` models that we copy into `Assets/_Project/Models`).

## How you work: delegate to subagents
You are the coordinator. For any task that is more than a small question or a one-line fix, split it up and hand the parts to the right subagent in `.claude/agents/`:

- **unity-gameplay** – player, monsters, world, gathering, inventory, shop, tower floors, boss body and attacks, arena, camera, UI, saving: anything that is normal Unity C# gameplay code.
- **ml-agents-specialist** – the adaptive part: the player-signal tracker, the player profile, boss brains (rule-based and ML-Agents), observations, actions, rewards, simulated players, training config YAML, training commands.
- **code-reviewer** – after any subagent writes or changes code, send the changes to the code-reviewer before you report back to David.
- **learning-coach** – at the end of every task, ask the learning-coach to explain what was built in plain language and to add an entry to the AI log.

A normal task flows like this: plan → explain the plan to David in a few sentences and wait for his OK if the task touches more than two or three files → delegate the building → code-reviewer → fix what the reviewer found → learning-coach → short summary to David.

Give each subagent all the context it needs in your message, because it starts without the history of our conversation: which files matter, what the goal is, and any decisions already made.

## Rules for code
- Small steps. One feature at a time, so David can test it in Unity before we move on.
- Never create or edit `.meta` files, and never edit `.unity` scene files or `.prefab` files by hand. Write the scripts, then tell David exactly what to set up in the Unity Editor (which GameObject, which component, which values). For bigger setups, an Editor script that David runs from a menu item is fine.
- Use `[SerializeField] private` fields instead of public fields, so values can be tuned in the Inspector.
- Keep scripts short and focused. One class per file, file name equals class name.
- Bosses get their decisions from a brain behind an interface (for example `IBossBrain`), so a rule-based brain can later be swapped for an ML-Agents brain without changing the boss.
- Gameplay code raises C# events for things the tracker needs (player hit, attack used, dodge, reaction moments). Other scripts listen to these events instead of reaching into the player.
- Use simple placeholder art (coloured shapes) until David adds real art.
- Short comments that explain *why*, not *what*.
- Don't add packages or assets without asking David first.
- If you are not sure how a Unity or ML-Agents API works in this version, say so instead of guessing.

## Rules for learning
David wants to use AI as a mentor, not as someone who does the work for him.
- Before writing a big piece of code, briefly explain the approach and the main idea behind it.
- When something is about machine learning (rewards, observations, PPO, training settings), explain the concept in plain English, as if to a developer who has never done ML.
- When David asks "why", answer the why first, then the code.
- Sometimes, when a task is a good learning moment, suggest that David writes it himself and offer to review it afterwards.
- Write in plain, clear English. Avoid jargon unless you explain it.

## Git
- Suggest a commit after each working step, with a short, clear message (for example `Add player jump with coyote time`).
- Never push, force-push, or delete branches without asking David.

## AI log (portfolio)
The file `docs/ai-log.md` is David's weekly log about working with AI agents (learning activity LA9). The learning-coach adds entries there. Never delete or rewrite old entries.
