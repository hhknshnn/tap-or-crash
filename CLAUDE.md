# CLAUDE.md

# Tap or Crash
Production Development Guide

This document defines the permanent engineering rules for this repository.

Unless the current task explicitly overrides a rule, ALWAYS follow this document.

TASK EXECUTION PROTOCOL

Before doing any work:

1. Break the task into a numbered checklist.

2. Print the checklist.

3. Mark each item as completed as you finish it.

4. If any step is expected to take longer than 5 minutes,
pause and report your current progress before continuing.

5. If a blocker appears,
stop immediately,
report the blocker,
and wait for approval.

Never work silently for a long period.

------------------------------------------------------------
PROJECT PHILOSOPHY
------------------------------------------------------------

This is a production game.

Everything should move the project closer to release quality.

Never prototype.

Never leave temporary implementations.

Never introduce dead code.

Never implement "good enough".

Every implementation should be maintainable.

Long-term maintainability always wins.

------------------------------------------------------------
PRIMARY OBJECTIVE
------------------------------------------------------------

Always optimize for:

1. Stability
2. Maintainability
3. Readability
4. Mobile performance
5. Production quality

Implementation speed is never more important than architecture.

------------------------------------------------------------
WORKING STYLE
------------------------------------------------------------

Work on ONE task only.

Never start another feature while the current one is unfinished.

Never increase task scope without approval.

Implement only what was requested.

Do not add "nice to have" improvements.

Do not redesign systems unless explicitly requested.

------------------------------------------------------------
BLOCKER POLICY (VERY IMPORTANT)
------------------------------------------------------------

If a blocker appears:

STOP IMMEDIATELY.

Do NOT:

- spend a long time investigating
- redesign surrounding systems
- implement multiple alternatives
- continue guessing

Instead:

1. Explain the blocker.
2. Explain why it blocks the task.
3. Present the available options.
4. Wait for approval.

Never spend more than approximately 5–10 minutes investigating a blocker without reporting it.

------------------------------------------------------------
ARCHITECTURE
------------------------------------------------------------

Always prefer:

- modular systems
- reusable systems
- isolated systems
- data-driven systems

Avoid:

- duplicated logic
- hidden dependencies
- circular references
- unnecessary abstractions

Architecture is more important than implementation speed.

------------------------------------------------------------
GAMEPLAY VS PRESENTATION
------------------------------------------------------------

Gameplay and Presentation are COMPLETELY SEPARATE.

Presentation must NEVER become a gameplay dependency.

Gameplay must NEVER become a presentation dependency.

Examples:

Gameplay:

- Rocket
- Physics
- Scoring
- Progression
- Planet spawning
- Continue system

Presentation:

- Hero Planet
- Camera
- Backgrounds
- UI
- Particles
- Menu
- Animations
- Transitions

Never mix these systems.

------------------------------------------------------------
GAMEMANAGER
------------------------------------------------------------

GameManager is NOT a dumping ground.

Never move gameplay logic into GameManager.

Create dedicated systems instead.

------------------------------------------------------------
DATA-DRIVEN DESIGN
------------------------------------------------------------

Whenever possible:

Prefer configuration over code.

Prefer ScriptableObjects.

Avoid hardcoded values.

Avoid duplicated constants.

Centralize shared configuration.

------------------------------------------------------------
UNITY SAFETY
------------------------------------------------------------

Never edit:

Library/

Temp/

Obj/

Logs/

Generated cache

Only modify:

Assets/

Packages/

ProjectSettings/

Only when necessary.

------------------------------------------------------------
MCP WORKFLOW
------------------------------------------------------------

Before starting any task:

Verify:

✓ Blender MCP

✓ Unity MCP

✓ Unity Editor running

✓ Blender running

✓ Project loaded

✓ Zero compile errors

If any verification fails:

STOP.

Report the issue.

------------------------------------------------------------
BLENDER RULES
------------------------------------------------------------

Concept art is inspiration.

Never copy every tiny detail literally.

Simplify geometry.

Never simplify artistic identity.

Maintain:

- silhouette
- composition
- color harmony
- visual identity

Always preserve:

- centered pivot
- clean topology
- mobile optimization

------------------------------------------------------------
UNITY RULES
------------------------------------------------------------

Never modify gameplay while implementing presentation.

Never modify unrelated systems.

Never rename assets unnecessarily.

Never move files without reason.

Never change project-wide settings unless requested.

------------------------------------------------------------
PERFORMANCE
------------------------------------------------------------

Target mobile devices.

Prefer:

- low draw calls
- reusable materials
- shared textures
- optimized meshes

Do NOT optimize prematurely.

Correctness first.

Optimization second.

------------------------------------------------------------
EXISTING CODE
------------------------------------------------------------

Before creating new code:

Search for an existing implementation.

Reuse existing systems whenever possible.

Avoid duplicated functionality.

------------------------------------------------------------
DEBUGGING
------------------------------------------------------------

When fixing bugs:

1. Find the smallest root cause.
2. Apply the smallest safe fix.
3. Verify the result.
4. Report possible side effects.

Never rewrite unrelated systems.

------------------------------------------------------------
VALIDATION
------------------------------------------------------------

After every task verify:

✓ Compile

✓ Console

✓ Missing references

✓ Missing materials

✓ Prefabs

✓ Runtime behaviour

Never assume.

Always verify.

------------------------------------------------------------
REPORT FORMAT
------------------------------------------------------------

Keep reports concise.

Return only:

✓ Root Cause (if applicable)

✓ Files Created

✓ Files Modified

✓ Validation

✓ Remaining Issues

Avoid unnecessary essays.

------------------------------------------------------------
COMMUNICATION
------------------------------------------------------------

If requirements are unclear:

Ask.

Never guess.

If multiple implementations exist:

Explain them briefly.

Wait for approval.

------------------------------------------------------------
CODE STYLE
------------------------------------------------------------

Prefer:

- readable code
- explicit names
- small methods
- predictable behaviour

Avoid:

- clever tricks
- hidden side effects
- magic numbers
- unnecessary inheritance

------------------------------------------------------------
GIT
------------------------------------------------------------

Never:

- commit automatically
- delete files automatically
- overwrite user work

Always wait for confirmation.

------------------------------------------------------------
TAP OR CRASH SPECIFIC RULES
------------------------------------------------------------

Planet Theme System is considered production complete.

World Transition is production complete.

Do not modify those systems unless explicitly requested.

Current locked roadmap:

1. Main Menu Presentation
2. In-Game HUD Presentation
3. Gameplay Juice Pass
4. Endless Mode

Do not begin a later phase before the current phase is complete.

------------------------------------------------------------
HERO PLANET
------------------------------------------------------------

Hero Planet exists ONLY in the Main Menu.

Hero Planet is NOT a gameplay planet.

Never borrow gameplay planets for the Main Menu.

Hero Planet must remain completely independent.

Gameplay starts with the real gameplay planet.

------------------------------------------------------------
MAIN MENU
------------------------------------------------------------

The Main Menu is presentation only.

Do not change gameplay architecture while improving the Main Menu.

Camera, lighting, Hero Planet, rocket presentation and UI belong to Presentation.

------------------------------------------------------------
ASSET CREATION
------------------------------------------------------------

When using Blender MCP:

1. Analyze reference images first.
2. Explain detected landmarks.
3. Model afterwards.

Never start modeling before understanding the reference.

------------------------------------------------------------
INVESTIGATION LIMIT
------------------------------------------------------------

Avoid long autonomous investigations.

If the task expands beyond its original scope:

STOP.

Explain why.

Wait for approval.

The user makes product decisions.

Claude makes engineering decisions.

------------------------------------------------------------
FINAL PRINCIPLE
------------------------------------------------------------

When in doubt choose the solution that is:

- simpler
- cleaner
- easier to understand
- easier to maintain
- production quality

Never sacrifice long-term maintainability for short-term convenience.

Visual asset improvements are NOT considered gameplay changes.

Updating meshes, materials, textures or presentation-only visuals is allowed as long as:

- gameplay logic
- colliders
- physics
- timing
- progression

remain unchanged.