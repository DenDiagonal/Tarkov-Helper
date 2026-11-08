import json
import sqlite3
import os

core_path = r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper\Assets\DB\Data\tasks_core.json"
obj_path = r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper\Assets\DB\Data\tasks_objectives.json"
rew_path = r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper\Assets\DB\Data\tasks_rewards.json"

KNOWN_DBS = [
    r"C:\Games\Proyectos\Tarkov-Helper\publish\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\publish\Assets\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovDBEditor\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovDBEditor\Additional\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovDBEditor\bin\Debug\net8.0-windows\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovDBEditor\bin\Release\net8.0-windows\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper\Assets\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper\bin\Debug\net8.0-windows\Assets\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper\bin\Release\net8.0-windows\Assets\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper\bin\Release\net8.0-windows\win-x64\Assets\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper.Tests\bin\Debug\net8.0-windows\Assets\tarkov_data.db",
    r"C:\Games\Proyectos\Tarkov-Helper\TarkovHelper.Tests\bin\Release\net8.0-windows\Assets\tarkov_data.db"
]

MAP_NORMALIZATION = {
    "ground zero": "GroundZero",
    "streets of tarkov": "StreetsOfTarkov",
    "customs": "Customs",
    "factory": "Factory",
    "woods": "Woods",
    "shoreline": "Shoreline",
    "interchange": "Interchange",
    "reserve": "Reserve",
    "lighthouse": "Lighthouse",
    "the lab": "Labs",
    "the labs": "Labs",
    "labs": "Labs",
    "any": "Any",
    "any map": "Any"
}

TYPE_NORMALIZATION = {
    "visit": "Visit",
    "mark": "Mark",
    "plantitem": "Stash",
    "giveitem": "HandOver",
    "givequestitem": "HandOver",
    "finditem": "Collect",
    "findquestitem": "Collect",
    "shoot": "Kill",
    "kill": "Kill",
    "extract": "Survive",
    "survive": "Survive",
    "buildweapon": "Custom",
    "taskstatus": "Prerequisite",
    "useitem": "Custom",
    "skill": "Custom",
    "traderstanding": "Custom",
    "traderlevel": "Custom"
}

def normalize_map(name):
    if not name: return "Any"
    return MAP_NORMALIZATION.get(name.strip().lower(), name.strip())

def normalize_type(t):
    if not t: return "Custom"
    return TYPE_NORMALIZATION.get(t.strip().lower(), "Custom")

with open(core_path, "r", encoding="utf-8") as f:
    core_tasks = json.load(f)["data"]["tasks"]

with open(obj_path, "r", encoding="utf-8") as f:
    obj_tasks = {t["id"]: t for t in json.load(f)["data"]["tasks"]}

with open(rew_path, "r", encoding="utf-8") as f:
    rew_tasks = {t["id"]: t for t in json.load(f)["data"]["tasks"]}

# Pre-build item name cache from first DB
item_name_cache = {}
first_db = next((p for p in KNOWN_DBS if os.path.exists(p)), None)
if first_db:
    c = sqlite3.connect(first_db)
    cur = c.cursor()
    cur.execute("SELECT Id, Name FROM Items")
    for r in cur.fetchall():
        item_name_cache[r[0]] = r[1]
    c.close()

def get_item_name(item_id, fallback="Unknown Item"):
    return item_name_cache.get(item_id, fallback)

def sync_block_full(start_idx, end_idx):
    target_tasks = core_tasks[start_idx:end_idx]
    actual_end = min(end_idx, len(core_tasks))
    print(f"\n=======================================================")
    print(f"AUDITING & SYNCING BLOCK: Tasks {start_idx + 1} to {actual_end} (Count: {len(target_tasks)})")
    print(f"=======================================================")
    
    active_dbs = [p for p in KNOWN_DBS if os.path.exists(p)]
    print(f"Targeting {len(active_dbs)} active databases.")
    
    report_changes = []
    
    for task in target_tasks:
        tid = task["id"]
        tname = task["name"]
        can_trader = task.get("trader", {}).get("name") or "Unknown"
        can_map = normalize_map(task.get("map", {}).get("name") if task.get("map") else "Any")
        can_level = task.get("minPlayerLevel") or 0
        can_karma = task.get("minScavKarma") or 0
        can_kappa = 1 if task.get("kappaRequired") else 0
        can_wiki = task.get("wikiLink") or ""
        can_objs = obj_tasks.get(tid, {}).get("objectives", [])
        can_reqs = task.get("taskRequirements", [])
        
        # Collect required items for this task
        can_req_items = []
        for o in can_objs:
            otype = o.get("type")
            items = o.get("items", [])
            m_item = o.get("markerItem")
            count = o.get("count", 1)
            fir = 1 if o.get("foundInRaid") else 0
            dogtag_lvl = o.get("dogTagLevel")
            
            if otype in ["giveItem", "giveQuestItem"]:
                for it in items:
                    iid = it.get("id")
                    if iid:
                        can_req_items.append({
                            "id": f"{tid}_{iid}_handover",
                            "itemId": iid,
                            "itemName": get_item_name(iid),
                            "count": count,
                            "fir": fir,
                            "type": "Handover",
                            "dogtag": dogtag_lvl
                        })
            elif otype == "plantItem":
                for it in items:
                    iid = it.get("id")
                    if iid:
                        can_req_items.append({
                            "id": f"{tid}_{iid}_required",
                            "itemId": iid,
                            "itemName": get_item_name(iid),
                            "count": count,
                            "fir": fir,
                            "type": "Required",
                            "dogtag": dogtag_lvl
                        })
            elif m_item:
                iid = m_item.get("id")
                if iid:
                    can_req_items.append({
                        "id": f"{tid}_{iid}_marker",
                        "itemId": iid,
                        "itemName": get_item_name(iid, "MS2000 Marker"),
                        "count": count,
                        "fir": 0,
                        "type": "Required",
                        "dogtag": None
                    })
                    
        task_modifications = []
        
        for db_idx, db_path in enumerate(active_dbs):
            try:
                conn = sqlite3.connect(db_path)
                cur = conn.cursor()
                
                # 1. Quests table
                cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='Quests'")
                if cur.fetchone():
                    cur.execute("SELECT Id, Trader, Location, MinLevel, KappaRequired FROM Quests WHERE Id = ?", (tid,))
                    q_row = cur.fetchone()
                    if q_row:
                        old_trader, old_loc, old_lvl, old_kap = q_row[1], q_row[2], q_row[3], q_row[4]
                        if old_loc != can_map or old_lvl != can_level or old_kap != can_kappa or old_trader != can_trader:
                            cur.execute("""
                                UPDATE Quests 
                                SET Trader = ?, Location = ?, MinLevel = ?, KappaRequired = ?, WikiPageLink = ?, UpdatedAt = datetime('now')
                                WHERE Id = ?
                            """, (can_trader, can_map, can_level, can_kappa, can_wiki, tid))
                            if db_idx == 0:
                                task_modifications.append(f"Header: Loc '{old_loc}'->'{can_map}', Lvl {old_lvl}->{can_level}, Kap {old_kap}->{can_kappa}")
                    else:
                        cur.execute("""
                            INSERT INTO Quests (Id, Name, NameEN, Trader, Location, MinLevel, KappaRequired, WikiPageLink, UpdatedAt, IsApproved)
                            VALUES (?, ?, ?, ?, ?, ?, ?, ?, datetime('now'), 1)
                        """, (tid, tname, tname, can_trader, can_map, can_level, can_kappa, can_wiki))
                        if db_idx == 0:
                            task_modifications.append("Inserted Quest in Quests table")
                            
                # 2. QuestObjectives table
                cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='QuestObjectives'")
                if cur.fetchone():
                    cur.execute("PRAGMA table_info(QuestObjectives)")
                    obj_cols = [c[1] for c in cur.fetchall()]
                    has_customized = 'IsCustomized' in obj_cols
                    
                    cur.execute("SELECT Id, Description, IsCustomized FROM QuestObjectives WHERE QuestId = ?", (tid,))
                    existing_objs = {row[0]: (row[1], row[2] if has_customized else 0) for row in cur.fetchall()}
                    can_obj_ids = set()
                    
                    for sort_idx, obj in enumerate(can_objs):
                        oid = obj["id"]
                        can_obj_ids.add(oid)
                        otype = normalize_type(obj.get("type"))
                        odesc = obj.get("description") or ""
                        ocount = obj.get("count") or 1
                        ofir = 1 if obj.get("foundInRaid") else 0
                        omap = can_map
                        
                        loc_points_json = None
                        if obj.get("zones"):
                            pts = []
                            for z in obj["zones"]:
                                pos = z.get("position")
                                if pos:
                                    pts.append({"X": pos.get("x", 0), "Y": pos.get("y", 0), "Z": pos.get("z", 0), "FloorId": "main"})
                            if pts:
                                loc_points_json = json.dumps(pts)
                                
                        if oid in existing_objs:
                            is_custom = existing_objs[oid][1] if has_customized else 0
                            if is_custom == 1:
                                cur.execute("""
                                    UPDATE QuestObjectives
                                    SET SortOrder = ?, ObjectiveType = ?, Description = ?, TargetCount = ?, RequiresFIR = ?, MapName = ?, UpdatedAt = datetime('now')
                                    WHERE Id = ?
                                """, (sort_idx, otype, odesc, ocount, ofir, omap, oid))
                            else:
                                if loc_points_json:
                                    cur.execute("""
                                        UPDATE QuestObjectives
                                        SET SortOrder = ?, ObjectiveType = ?, Description = ?, TargetCount = ?, RequiresFIR = ?, MapName = ?, LocationPoints = ?, UpdatedAt = datetime('now')
                                        WHERE Id = ?
                                    """, (sort_idx, otype, odesc, ocount, ofir, omap, loc_points_json, oid))
                                else:
                                    cur.execute("""
                                        UPDATE QuestObjectives
                                        SET SortOrder = ?, ObjectiveType = ?, Description = ?, TargetCount = ?, RequiresFIR = ?, MapName = ?, UpdatedAt = datetime('now')
                                        WHERE Id = ?
                                    """, (sort_idx, otype, odesc, ocount, ofir, omap, oid))
                        else:
                            if has_customized:
                                cur.execute("""
                                    INSERT OR REPLACE INTO QuestObjectives (Id, QuestId, SortOrder, ObjectiveType, Description, TargetCount, RequiresFIR, MapName, LocationPoints, IsApproved, UpdatedAt, IsCustomized)
                                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1, datetime('now'), 0)
                                """, (oid, tid, sort_idx, otype, odesc, ocount, ofir, omap, loc_points_json))
                            else:
                                cur.execute("""
                                    INSERT OR REPLACE INTO QuestObjectives (Id, QuestId, SortOrder, ObjectiveType, Description, TargetCount, RequiresFIR, MapName, LocationPoints, IsApproved, UpdatedAt)
                                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1, datetime('now'))
                                """, (oid, tid, sort_idx, otype, odesc, ocount, ofir, omap, loc_points_json))
                            if db_idx == 0:
                                task_modifications.append(f"Obj Added: [{otype}] {odesc}")
                                
                    # Prune stale
                    for ex_id, (ex_desc, ex_cust) in existing_objs.items():
                        if ex_id not in can_obj_ids and ex_cust != 1:
                            cur.execute("DELETE FROM QuestObjectives WHERE Id = ?", (ex_id,))
                            if db_idx == 0:
                                task_modifications.append(f"Obj Removed (Stale): {ex_id} ({ex_desc})")
                                
                # 3. QuestRequirements (Prerequisites)
                cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='QuestRequirements'")
                if cur.fetchone():
                    cur.execute("DELETE FROM QuestRequirements WHERE QuestId = ?", (tid,))
                    for req in can_reqs:
                        req_task_id = req.get("task", {}).get("id")
                        if req_task_id:
                            req_statuses = req.get("status", ["complete"])
                            status_str = req_statuses[0] if req_statuses else "complete"
                            req_id = f"{tid}_{req_task_id}"
                            cur.execute("""
                                INSERT OR IGNORE INTO QuestRequirements (Id, QuestId, RequiredQuestId, RequirementType, GroupId, IsApproved, UpdatedAt)
                                VALUES (?, ?, ?, ?, 0, 1, datetime('now'))
                            """, (req_id, tid, req_task_id, status_str))
                            
                # 4. QuestRequiredItems
                cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='QuestRequiredItems'")
                if cur.fetchone() and len(can_req_items) > 0:
                    # Clean existing items that are not in can_req_items
                    can_item_ids = {cri["itemId"] for cri in can_req_items}
                    cur.execute("SELECT Id, ItemId, ItemName FROM QuestRequiredItems WHERE QuestId = ?", (tid,))
                    db_items = cur.fetchall()
                    for dbi in db_items:
                        if dbi[1] not in can_item_ids:
                            cur.execute("DELETE FROM QuestRequiredItems WHERE Id = ?", (dbi[0],))
                            if db_idx == 0:
                                task_modifications.append(f"Item Removed: {dbi[2]} ({dbi[1]})")
                                
                    for idx_item, cri in enumerate(can_req_items):
                        cur.execute("""
                            INSERT OR REPLACE INTO QuestRequiredItems (Id, QuestId, ItemId, ItemName, Count, RequiresFIR, RequirementType, SortOrder, DogtagMinLevel, IsApproved, UpdatedAt)
                            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1, datetime('now'))
                        """, (cri["id"], tid, cri["itemId"], cri["itemName"], cri["count"], cri["fir"], cri["type"], idx_item, cri["dogtag"]))
                        
                conn.commit()
                conn.close()
            except Exception as e:
                print(f"Error on {db_path} for {tname}: {e}")
                
        if task_modifications:
            report_changes.append({
                "name": tname,
                "id": tid,
                "trader": can_trader,
                "map": can_map,
                "modifications": task_modifications
            })
            
    print(f"\nBlock Summary ({start_idx + 1} - {actual_end}):")
    print(f"Total tasks processed: {len(target_tasks)}")
    print(f"Tasks updated: {len(report_changes)}")
    for item in report_changes[:15]:
        print(f"  * {item['name']} ({item['trader']} - {item['map']}):")
        for m in item["modifications"]:
            print(f"      - {m}")
    if len(report_changes) > 15:
        print(f"  ... and {len(report_changes) - 15} more tasks updated.")
            
    return report_changes

if __name__ == "__main__":
    import sys
    start = int(sys.argv[1]) if len(sys.argv) > 1 else 0
    end = int(sys.argv[2]) if len(sys.argv) > 2 else 50
    sync_block_full(start, end)
