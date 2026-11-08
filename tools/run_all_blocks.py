import sys
import os
import json
from sync_quest_block_complete import sync_block_full, core_tasks

def run_all():
    total_tasks = len(core_tasks)
    block_size = 50
    num_blocks = (total_tasks + block_size - 1) // block_size
    
    print(f"===========================================================")
    print(f"STARTING FULL AUDIT & SYNC OF ALL {total_tasks} CANONICAL TASKS")
    print(f"DIVIDED INTO {num_blocks} BLOCKS OF 50")
    print(f"===========================================================\n")
    
    block_summaries = []
    
    for b in range(num_blocks):
        start = b * block_size
        end = min((b + 1) * block_size, total_tasks)
        print(f"\n>>> EXECUTING BLOQUE {b + 1}/{num_blocks} (Tasks {start + 1} to {end})...")
        changes = sync_block_full(start, end)
        block_summaries.append({
            "block": b + 1,
            "range": f"{start + 1} - {end}",
            "tasks_count": end - start,
            "updated_count": len(changes),
            "updated_tasks": [c["name"] for c in changes]
        })
        
    print(f"\n===========================================================")
    print("ALL BLOCKS COMPLETED SUCCESSFULLY!")
    print(f"===========================================================")
    for bs in block_summaries:
        print(f"Bloque {bs['block']} ({bs['range']}): {bs['updated_count']}/{bs['tasks_count']} tareas corregidas.")
        
    # Write summary log
    with open(r"C:\Games\Proyectos\Tarkov-Helper\scratch\audit_summary.json", "w", encoding="utf-8") as f:
        json.dump(block_summaries, f, indent=2)

if __name__ == "__main__":
    run_all()
