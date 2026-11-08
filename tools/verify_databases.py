import os
import sys
import sqlite3

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))

TARGET_DATABASES = [
    os.path.join(REPO_ROOT, "publish", "Assets", "tarkov_data.db"),
    os.path.join(REPO_ROOT, "publish", "tarkov_data.db"),
    os.path.join(REPO_ROOT, "TarkovHelper", "Assets", "tarkov_data.db"),
    os.path.join(REPO_ROOT, "TarkovHelper", "tarkov_data.db")
]

USER_DATA_DB = os.path.expandvars(r"%APPDATA%\TarkovHelper\Config\user_data.db")

FAKE_SURVIVE_TARGETS = [
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

def verify_all():
    print("=" * 80)
    print("MULTI-LOCATION DATABASE VERIFICATION REPORT")
    print("=" * 80)

    all_passed = True

    # 1. Verify user_data.db isolation
    print("\n[CHECK 1] Verifying user_data.db isolation...")
    if os.path.exists(USER_DATA_DB):
        st = os.stat(USER_DATA_DB)
        print(f"  PASS: user_data.db located at {USER_DATA_DB} (Size: {st.st_size} bytes)")
    else:
        print("  NOTICE: user_data.db does not exist in standard AppData location")

    # 2. Verify each database file
    print("\n[CHECK 2] Verifying target databases...")
    for db_path in TARGET_DATABASES:
        rel_path = os.path.relpath(db_path, REPO_ROOT)
        print(f"\n--- Checking {rel_path} ---")
        if not os.path.exists(db_path):
            print(f"  FAIL: Database not found: {rel_path}")
            all_passed = False
            continue

        conn = sqlite3.connect(db_path)
        cur = conn.cursor()

        # Quests count (571 canonical quests)
        cur.execute("SELECT count(*) FROM Quests;")
        quests_cnt = cur.fetchone()[0]
        quests_ok = (quests_cnt == 571)

        # QuestTraderRequirements count (expected ~304)
        cur.execute("SELECT count(*) FROM QuestTraderRequirements;")
        trader_req_cnt = cur.fetchone()[0]
        trader_ok = (300 <= trader_req_cnt <= 305)

        # QuestPoolRequirements count (expected 168)
        cur.execute("SELECT count(*) FROM QuestPoolRequirements;")
        pool_req_cnt = cur.fetchone()[0]
        pool_ok = (pool_req_cnt == 168)

        # QuestObjectives calibration points count (expected exactly 558)
        cur.execute("SELECT count(*) FROM QuestObjectives WHERE OriginalLocationPoints IS NOT NULL;")
        calib_cnt = cur.fetchone()[0]
        calib_ok = (calib_cnt == 558)

        # Zero unmapped objectives with coordinates
        cur.execute("""
            SELECT count(*) FROM QuestObjectives
            WHERE (LocationPoints IS NOT NULL AND LocationPoints != '' AND LocationPoints != '[]')
              AND (MapName IS NULL OR MapName = 'None' OR MapName = '');
        """)
        unmapped_coords_cnt = cur.fetchone()[0]
        unmapped_coords_ok = (unmapped_coords_cnt == 0)

        # Integrity check
        cur.execute("PRAGMA integrity_check;")
        integrity = cur.fetchone()[0]
        integrity_ok = (integrity == "ok")

        # Reserve Expert check (no cargo manifests)
        cur.execute("""
            SELECT count(*) FROM QuestRequiredItems
            WHERE QuestId = '608974af4b05530f55550c21'
              AND ItemId IN ('f46ff229a54d5c1a1ba5cbb4', 'e8eae5078102c45c2cf45ea3', '630e5e15b068b3b8a894a4fc');
        """)
        re_manifests = cur.fetchone()[0]
        re_clean = (re_manifests == 0)

        # Reserve Expert objectives count (7 canonical)
        cur.execute("SELECT count(*) FROM QuestObjectives WHERE QuestId = '608974af4b05530f55550c21';")
        re_objs_cnt = cur.fetchone()[0]
        re_objs_ok = (re_objs_cnt == 7)

        # Crooked Cop check (Kollontay spawns: MVD Academy and Klimov Mall)
        cur.execute("""
            SELECT MapName, LocationPoints, OptionalPoints
            FROM QuestObjectives
            WHERE QuestId = '6578eb36e5020875d64645cd' AND SortOrder = 0;
        """)
        cc_row = cur.fetchone()
        cc_ok = (
            cc_row is not None and
            cc_row[0] == "StreetsOfTarkov" and
            cc_row[1] is not None and "-255.01" in cc_row[1] and
            cc_row[2] is not None and "-153.59" in cc_row[2]
        )

        # Zero fake survive objectives in sanitized targets
        q_placeholders = ",".join(f"'{q}'" for q in FAKE_SURVIVE_TARGETS)
        cur.execute(f"""
            SELECT count(*) FROM QuestObjectives
            WHERE QuestId IN ({q_placeholders})
              AND (ObjectiveType = 'Survive' OR Description LIKE '%Survive and extract%');
        """)
        fake_survive_cnt = cur.fetchone()[0]
        fake_survive_ok = (fake_survive_cnt == 0)

        conn.close()

        print(f"  Quests:                  {quests_cnt} ({'PASS' if quests_ok else 'FAIL'})")
        print(f"  QuestTraderRequirements: {trader_req_cnt} (Target: ~303 -> {'PASS' if trader_ok else 'FAIL'})")
        print(f"  QuestPoolRequirements:   {pool_req_cnt} (Target: 168 -> {'PASS' if pool_ok else 'FAIL'})")
        print(f"  OriginalLocationPoints:  {calib_cnt} (Target: 558 -> {'PASS' if calib_ok else 'FAIL'})")
        print(f"  Unmapped Objectives:     {unmapped_coords_cnt} ({'PASS (0 unmapped)' if unmapped_coords_ok else f'FAIL ({unmapped_coords_cnt} unmapped)'})")
        print(f"  Crooked Cop Spawns:      {'PASS (MVD & Klimov)' if cc_ok else 'FAIL'}")
        print(f"  Reserve Expert Clean:    {'PASS (0 corrupt manifests)' if re_clean else 'FAIL'}")
        print(f"  Reserve Expert Objs:     {re_objs_cnt} ({'PASS (7 canonical)' if re_objs_ok else 'FAIL'})")
        print(f"  Fake Survive Purged:     {'PASS (0 fake survive objectives)' if fake_survive_ok else f'FAIL ({fake_survive_cnt} remain)'}")
        print(f"  PRAGMA integrity_check:  {integrity} ({'PASS' if integrity_ok else 'FAIL'})")

        if not (quests_ok and trader_ok and pool_ok and calib_ok and unmapped_coords_ok and cc_ok and re_clean and re_objs_ok and fake_survive_ok and integrity_ok):
            all_passed = False

    print("\n" + "=" * 80)
    if all_passed:
        print("OVERALL VERIFICATION: ALL INVARIANTS AND REQUIREMENTS PASSED (100% SUCCESS)")
    else:
        print("OVERALL VERIFICATION: ONE OR MORE CHECKS FAILED")
    print("=" * 80)
    return all_passed

if __name__ == "__main__":
    success = verify_all()
    sys.exit(0 if success else 1)
