# CLAUDE.md

## Project Overview
This is a Unity 6 2D mobile game project named **tap-or-crash**.
Core gameplay appears to involve a rocket/player object, planets/obstacles, camera follow, and spawn management.

## Main Goal
When making changes, prioritize:
1. Keeping the game playable at all times
2. Avoiding breaking scene references or prefab links
3. Making small, reversible changes
4. Preserving existing gameplay feel unless explicitly asked to change it

## Tech Stack
- Unity 6
- C#
- 2D project
- TextMesh Pro

## Important Scripts
These are likely key gameplay scripts and should be edited carefully:
- `Assets/CameraFollow.cs`
- `Assets/GameManager.cs`
- `Assets/PlanetSpawner.cs`
- `Assets/RocketController.cs`

## Working Rules
- Do not rename scripts, folders, scenes, prefabs, tags, layers, or serialized fields unless explicitly requested.
- Do not move files between folders unless necessary.
- Prefer small targeted edits over broad refactors.
- Keep public field names stable to avoid breaking Inspector references.
- If a refactor is necessary, explain why before doing it.
- Do not delete assets or scenes unless explicitly requested.
- Preserve mobile-friendly performance.
- Keep code readable and simple.

## Unity Safety Rules
- Do not modify `Library`, `Temp`, or generated cache content.
- Only edit source files inside `Assets`, `Packages`, and `ProjectSettings` when needed.
- Avoid changing project-wide settings unless the task requires it.
- Avoid introducing new packages unless explicitly approved.
- If creating new scripts, place them in logical folders under `Assets`.

## Coding Style
- Use clear, short method names.
- Prefer serialized private fields over unnecessary public fields.
- Add brief comments only where logic is not obvious.
- Avoid overengineering.
- Keep MonoBehaviour responsibilities narrow.
- Use Unity lifecycle methods intentionally (`Awake`, `Start`, `Update`, etc.).
- Avoid hidden side effects across scripts.

## Gameplay Change Policy
When asked to add or adjust gameplay:
- First inspect the relevant script(s)
- Change as little as possible
- Preserve existing controls unless explicitly asked to redesign them
- Keep tuning values easy to edit in the Inspector
- If balancing gameplay, expose key values with `[SerializeField]`

## UI / UX Policy
- Keep UI simple and mobile-readable
- Avoid clutter
- Maintain consistency in naming and hierarchy
- Prefer minimal intrusive changes

## Debugging Policy
When fixing bugs:
1. Identify the smallest likely cause
2. Suggest the fix clearly
3. Apply the smallest safe code change
4. Mention possible side effects

## Request Handling
For each task:
- Briefly state what files are likely involved
- Then make the change
- Keep outputs concise and implementation-focused

## Things to Avoid
- Large unsolicited rewrites
- Renaming scene objects without instruction
- Changing input behavior unexpectedly
- Adding dependencies without approval
- Editing multiple unrelated systems in one pass

## Preferred Workflow
1. Read the task carefully
2. Inspect only the relevant files
3. Propose a minimal plan if the change is non-trivial
4. Implement the smallest clean solution
5. Summarize exactly what changed

## If Information Is Missing
If scene setup, prefab wiring, or expected behavior is unclear:
- State the uncertainty clearly
- Make the safest assumption
- Avoid destructive changes

## Output Style
- Be direct
- Be practical
- Prefer exact file-level changes
- Avoid long theory unless requested

