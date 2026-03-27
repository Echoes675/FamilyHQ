@AGENTS.md

**Read**: Make sure to be aware of the rules, information and skills detailed in .AGENTS.md

## Automatic Skill Loading

This project includes a `.agent/skills/` directory containing project-specific skills. When working on this project:

1. **Always read AGENTS.md first** to understand the project context and safety rules.
2. **Dynamically discover skills** by listing the contents of `.agent/skills/` at the start of each session.
3. **Automatically load relevant skills** from discovered skill directories based on task context.
4. **Skill precedence**: Project skills take precedence over global/default skills.
5. **Mandatory skill check**: Before any user-facing response, evaluate the user's request against all available skills (both global and project). If a skill applies, load and follow its SKILL.md instructions precisely.

### Skill Discovery Process
- Use `list_files(".agent/skills", recursive=true)` to discover all skill directories.
- For each skill directory, read the `SKILL.md` file to understand its purpose and triggers.
- When a task matches a skill's description or trigger keywords, automatically read the corresponding SKILL.md file.
- Follow the skill's instructions without requiring explicit user prompting.

### Skill Registration Rule
When creating a new skill:
1. Create the skill directory and `SKILL.md` file in `.agent/skills/`.
2. Update the "## Skills" section in `AGENTS.md` to include the new skill.
3. Ensure the skill follows the standard format with clear triggers and instructions.

