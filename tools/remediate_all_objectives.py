#!/usr/bin/env python3
"""
tools/remediate_all_objectives.py
Mass sanitization and canonical alignment of QuestObjectives in tarkov_data.db.

Actions performed:
1. Purge all 26 fake/extra "Survive and extract" objectives introduced by Wiki scraping
   where BSG has no extract requirement.
2. Replace fake survive objectives with the real in-game BSG objectives where applicable:
   - Wet Job - Part 3: Mark Artyom's car with MS2000 Marker.
   - Operation Aquarius - Part 1: Hand over water found in raid.
   - Easy Job: Eliminate Rogue Scavs around helicopter.
   - Pathfinder: Replace obsolete 2019 objective with 3 canonical Shoreline extracts.
   - Ironclad Proof: Align objectives with BSG (handover grenades + kill with grenades).
3. Re-index SortOrder to guarantee strictly sequential indices (0, 1, 2...).
4. Enforce strict invariant: All 474 OriginalLocationPoints calibration points preserved.
5. Replicate sanitized database to all 5 active repository locations.
"""

import os
import sys
import sqlite3
import json
import shutil

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))

TARGET_DATABASES = [
    os.path.join(REPO_ROOT, "TarkovHelper", "Assets", "tarkov_data.db"),
    os.path.join(REPO_ROOT, "publish", "Assets", "tarkov_data.db"),
    os.path.join(REPO_ROOT, "publish", "tarkov_data.db"),
    os.path.join(REPO_ROOT, "TarkovHelper", "tarkov_data.db"),
    os.path.join(REPO_ROOT, "TarkovDBEditor", "tarkov_data.db"),
    os.path.join(REPO_ROOT, "TarkovDBEditor", "Additional", "tarkov_data.db")
]

def remediate_single_db(db_path: str):
    if not os.path.exists(db_path):
        print(f"Skipping missing DB: {db_path}")
        return

    rel_name = os.path.relpath(db_path, REPO_ROOT)
    print(f"\n--- Saneando: {rel_name} ---")
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()

    # Verify initial calibration points count
    cur.execute("SELECT count(*) FROM QuestObjectives WHERE OriginalLocationPoints IS NOT NULL;")
    initial_calib = cur.fetchone()[0]

    # 1. Quests where fake survive is simply deleted (pure trailing junk):
    simple_delete_qids = [
        '608974d01a66564e74191fc0', # A Fuel Matter
        '5eda19f0edce541157209cee', # Anesthesia
        '5ae448e586f7744dcf0c2a67', # Big Sale
        '59c124d686f774189b3c843f', # Oil Run
        '64ee99639878a0569d6ec8c9', # Broadcast - Part 5
        '639dbaf17c898a131e1cffff', # Debtor
        '64f5aac4b63b74469b6c14c2', # Glory to CPSU
        '5a68665c86f774255929b4c7', # Health Care Privacy - Part 3
        '639135c3744e452011470807', # House Arrest
        '639136f086e646067c176a8b', # Kings of the Rooftops
        '64f731ab83cfca080a361e42', # Pets Won't Need It
        '63966ff54c3ef01b6f3ffad8', # Provocation
        '5edabd13218d181e29451442', # Rigged Game
        '5fd9fad9c1ce6b1a3b486d00', # Search Mission
        '5ac3467986f7741d6224abc2', # Ill-Wisher
        '5a03296886f774569778596a', # I Need More Power
        '6396700fea19ac7ed845db32', # Spotter
        '5ae448f286f77448d73c0131', # Fuel Crisis
        '5c10f94386f774227172c572', # Small Things, Big Help
        '5ede55112c95834b583f052a', # The Bunker
        '64ee9df4496db64f9b7a4432', # The Door
        '63967028c4a91c5cb76abd81', # Trouble in the Big City
    ]

    for qid in simple_delete_qids:
        cur.execute("""
            DELETE FROM QuestObjectives
            WHERE QuestId = ?
              AND (ObjectiveType = 'Survive' OR Description LIKE '%Survive and extract%')
              AND OriginalLocationPoints IS NULL
        """, (qid,))

    print(f"  [1] Purgados objetivos ficticios de supervivencia en {len(simple_delete_qids)} misiones.")

    # 2. Specific quest realignments:
    # 2.1 Wet Job - Part 3 (5a27bc1586f7741f6d40fa2f): BSG Obj 1 is marking car with MS2000
    cur.execute("""
        UPDATE QuestObjectives
        SET ObjectiveType = 'Mark',
            Description = 'Mark Artyom''s car with an MS2000 Marker on Shoreline',
            TargetType = 'Item',
            RequiresFIR = 0
        WHERE QuestId = '5a27bc1586f7741f6d40fa2f'
          AND (ObjectiveType = 'Survive' OR Description LIKE '%Survive and extract%');
    """)

    # 2.2 Operation Aquarius - Part 1 (59689fbd86f7740d137ebfc4): BSG Obj 1 is hand over water
    cur.execute("""
        UPDATE QuestObjectives
        SET ObjectiveType = 'HandOver',
            Description = 'Hand over the water found in dorms to Therapist',
            TargetType = 'Item',
            RequiresFIR = 1
        WHERE QuestId = '59689fbd86f7740d137ebfc4'
          AND (ObjectiveType = 'Survive' OR Description LIKE '%Survive and extract%');
    """)

    # 2.3 Easy Job (6179ac7511973d018217d0b9): BSG Obj 2 is eliminate Rogues
    cur.execute("""
        UPDATE QuestObjectives
        SET ObjectiveType = 'Kill',
            Description = 'Eliminate Rogue Scavs around the helicopter on Lighthouse',
            TargetType = 'Target',
            TargetCount = 3,
            RequiresFIR = 0
        WHERE QuestId = '6179ac7511973d018217d0b9'
          AND (ObjectiveType = 'Survive' OR Description LIKE '%Survive and extract%');
    """)

    # 2.4 Pathfinder (5ae449c386f7744bde357697): Replace obsolete 2019 quest with 3 canonical Shoreline exits
    cur.execute("DELETE FROM QuestObjectives WHERE QuestId = '5ae449c386f7744bde357697';")
    pathfinder_objs = [
        ("5bb60cbc88a45011a8235cc5", 0, "Visit", "Survive and extract through the Road to Customs extract on Shoreline", "Shoreline", "Shoreline"),
        ("6a60968c58aab7961885e537", 1, "Visit", "Survive and extract through the Lighthouse extract on Shoreline", "Shoreline", "Shoreline"),
        ("6a6096d81284478fd859003a", 2, "Visit", "Survive and extract through the Tunnel extract on Shoreline", "Shoreline", "Shoreline"),
    ]
    for oid, sorder, otype, odesc, mname, lname in pathfinder_objs:
        cur.execute("""
            INSERT INTO QuestObjectives (Id, QuestId, SortOrder, ObjectiveType, Description, MapName, LocationName, IsApproved)
            VALUES (?, '5ae449c386f7744bde357697', ?, ?, ?, ?, ?, 1)
        """, (oid, sorder, otype, odesc, mname, lname))

    # 2.5 Ironclad Proof (59ca1a6286f774509a270942): Align objectives (0: Handover grenades, 1: Kill scavs with grenades)
    cur.execute("""
        UPDATE QuestObjectives
        SET ObjectiveType = 'HandOver',
            Description = 'Hand over 10 M67 Hand grenades to Prapor'
        WHERE QuestId = '59ca1a6286f774509a270942' AND SortOrder = 0;
    """)
    cur.execute("""
        UPDATE QuestObjectives
        SET ObjectiveType = 'Kill',
            Description = 'Eliminate Scavs using hand grenades'
        WHERE QuestId = '59ca1a6286f774509a270942' AND SortOrder = 1;
    """)

    print("  [2] Reemplazados objetivos erróneos por especificaciones canónicas de BSG.")

    # 3. Re-index all SortOrders sequentially for all affected quests
    all_reindex_qids = simple_delete_qids + [
        '5a27bc1586f7741f6d40fa2f', '59689fbd86f7740d137ebfc4',
        '6179ac7511973d018217d0b9', '5ae449c386f7744bde357697',
        '59ca1a6286f774509a270942'
    ]
    for qid in all_reindex_qids:
        cur.execute("SELECT Id FROM QuestObjectives WHERE QuestId = ? ORDER BY SortOrder, rowid", (qid,))
        obj_ids = [r[0] for r in cur.fetchall()]
        for new_order, obj_id in enumerate(obj_ids):
            cur.execute("UPDATE QuestObjectives SET SortOrder = ? WHERE Id = ?", (new_order, obj_id))

    print(f"  [3] Re-indexados SortOrders de forma estrictamente secuencial (0..N-1).")

    # 4. Verify calibration invariant
    cur.execute("SELECT count(*) FROM QuestObjectives WHERE OriginalLocationPoints IS NOT NULL;")
    final_calib = cur.fetchone()[0]
    if final_calib != initial_calib:
        conn.rollback()
        raise ValueError(f"VIOLATION OF CALIBRATION INVARIANT! Initial: {initial_calib}, Final: {final_calib}")
    print(f"  [4] Invariante de calibración verificado: {final_calib} puntos intactos.")

    conn.commit()
    conn.close()

def main():
    print("=" * 80)
    print("PIPELINE DE SANEAMIENTO Y ALINEACIÓN CANÓNICA DE OBJETIVOS (BSG)")
    print("=" * 80)

    # 1. Remediate source asset
    primary_db = TARGET_DATABASES[0]
    remediate_single_db(primary_db)

    # 2. Replicate to all other targets
    for target in TARGET_DATABASES[1:]:
        if os.path.exists(os.path.dirname(target)):
            shutil.copy2(primary_db, target)
            print(f"Sincronizado {os.path.relpath(primary_db, REPO_ROOT)} -> {os.path.relpath(target, REPO_ROOT)}")

    print("\nSaneamiento y sincronización completados con éxito.")

if __name__ == "__main__":
    main()
