import json
import sqlite3
import os
import sys

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

MAP_ID_MAP = {
    "55f2d3fd4bdc2d5f408b4567": "Factory",
    "56f40101d2720b2a4d8b45d6": "Customs",
    "5704e3c2d2720bac5b8b4567": "Woods",
    "5704e4dad2720bb55b8b4567": "Lighthouse",
    "5704e554d2720bac5b8b456e": "Shoreline",
    "5704e5fad2720bc05b8b4567": "Reserve",
    "5714dbc024597771384a510d": "Interchange",
    "5714dc692459777137212e12": "StreetsOfTarkov",
    "59fc81d786f774390775787e": "Factory",
    "5b0fc42d86f7744a585f9105": "Labs",
    "653e6760052c01c1c805532f": "GroundZero",
    "65b8d6f5cdde2479cb2a3125": "GroundZero",
    "6733700029c367a3d40b02af": "Labyrinth",
    "69af492a4819ea4ba10a69c5": "Icebreaker"
}

NAME_TO_MAP = {
    "customs": "Customs",
    "factory": "Factory",
    "woods": "Woods",
    "lighthouse": "Lighthouse",
    "shoreline": "Shoreline",
    "reserve": "Reserve",
    "interchange": "Interchange",
    "streets of tarkov": "StreetsOfTarkov",
    "streetsoftarkov": "StreetsOfTarkov",
    "the lab": "Labs",
    "the labs": "Labs",
    "labs": "Labs",
    "ground zero": "GroundZero",
    "groundzero": "GroundZero",
    "the labyrinth": "Labyrinth",
    "labyrinth": "Labyrinth",
    "night factory": "Factory"
}

def resolve_objective_map(task_map, obj):
    if obj.get("zones"):
        for z in obj["zones"]:
            z_map_id = z.get("map", {}).get("id")
            if z_map_id in MAP_ID_MAP:
                return MAP_ID_MAP[z_map_id]
            z_map_name = z.get("map", {}).get("name")
            if z_map_name and z_map_name.lower() in NAME_TO_MAP:
                return NAME_TO_MAP[z_map_name.lower()]
    elif obj.get("possibleLocations"):
        for pl in obj["possibleLocations"]:
            pl_map_id = pl.get("map", {}).get("id")
            if pl_map_id in MAP_ID_MAP:
                return MAP_ID_MAP[pl_map_id]

    maps = obj.get("maps", [])
    if len(maps) == 1:
        m_id = maps[0].get("id")
        if m_id in MAP_ID_MAP:
            return MAP_ID_MAP[m_id]
        m_name = maps[0].get("name")
        if m_name and m_name.lower() in NAME_TO_MAP:
            return NAME_TO_MAP[m_name.lower()]

    desc = obj.get("description", "").lower()
    for phrase, target in [
        ("on streets of tarkov", "StreetsOfTarkov"),
        ("in streets of tarkov", "StreetsOfTarkov"),
        ("on ground zero", "GroundZero"),
        ("in ground zero", "GroundZero"),
        ("on customs", "Customs"),
        ("in customs", "Customs"),
        ("on factory", "Factory"),
        ("in factory", "Factory"),
        ("on woods", "Woods"),
        ("in woods", "Woods"),
        ("on shoreline", "Shoreline"),
        ("in shoreline", "Shoreline"),
        ("on reserve", "Reserve"),
        ("in reserve", "Reserve"),
        ("on lighthouse", "Lighthouse"),
        ("in lighthouse", "Lighthouse"),
        ("on interchange", "Interchange"),
        ("in interchange", "Interchange"),
        ("in the lab", "Labs"),
        ("on the lab", "Labs"),
        ("in the labyrinth", "Labyrinth"),
        ("on the labyrinth", "Labyrinth")
    ]:
        if phrase in desc:
            return target

    if task_map and task_map != "Any":
        return NAME_TO_MAP.get(task_map.lower(), task_map)

    return "Any"

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

active_dbs = [p for p in KNOWN_DBS if os.path.exists(p)]

def sync_single_block(block_num, start_idx, end_idx):
    target_tasks = core_tasks[start_idx:end_idx]
    actual_end = min(end_idx, len(core_tasks))
    print(f"\n=======================================================", flush=True)
    print(f"BLOQUE {block_num}: Tareas {start_idx + 1} a {actual_end} (Total en bloque: {len(target_tasks)})", flush=True)
    print(f"=======================================================", flush=True)
    
    # 1. Prepare all updates in memory for this block
    prepared_tasks = []
    
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
        
        # Prepare objectives
        prep_objs = []
        for sort_idx, obj in enumerate(can_objs):
            oid = obj["id"]
            if tid in ['6391d90f4ed9512be67647df', '6391d912f8e5dd32bf4e3ab2']:
                oid = f"{oid}_{tid[:8]}"
            otype = normalize_type(obj.get("type"))
            odesc = obj.get("description") or ""
            ocount = obj.get("count") or 1
            ofir = 1 if obj.get("foundInRaid") else 0
            omap = resolve_objective_map(can_map, obj)
            
            loc_points_json = None
            if obj.get("zones"):
                pts = []
                for z in obj["zones"]:
                    pos = z.get("position")
                    if pos:
                        pts.append({"X": pos.get("x", 0), "Y": pos.get("y", 0), "Z": pos.get("z", 0), "FloorId": "main"})
                if pts:
                    if len(pts) > 1:
                        xs = [p["X"] for p in pts]
                        zs = [p["Z"] for p in pts]
                        if (max(xs) - min(xs)) < 0.5 and (max(zs) - min(zs)) < 0.5:
                            pts = [pts[0]]
                    loc_points_json = json.dumps(pts)
            elif obj.get("possibleLocations"):
                pts = []
                for pl in obj["possibleLocations"]:
                    pl_map_id = pl.get("map", {}).get("id")
                    pl_map = MAP_ID_MAP.get(pl_map_id)
                    if not pl_map or pl_map == omap:
                        for pos in pl.get("positions", []):
                            pts.append({"X": pos.get("x", 0), "Y": pos.get("y", 0), "Z": pos.get("z", 0), "FloorId": "main"})
                if pts:
                    if len(pts) > 1:
                        xs = [p["X"] for p in pts]
                        zs = [p["Z"] for p in pts]
                        if (max(xs) - min(xs)) < 0.5 and (max(zs) - min(zs)) < 0.5:
                            pts = [pts[0]]
                    loc_points_json = json.dumps(pts)
            prep_objs.append((oid, sort_idx, otype, odesc, ocount, ofir, omap, loc_points_json))
            
        # Prepare prerequisites
        prep_reqs = []
        for req in can_reqs:
            req_task_id = req.get("task", {}).get("id")
            if req_task_id:
                req_statuses = req.get("status", ["complete"])
                status_str = req_statuses[0] if req_statuses else "complete"
                req_id = f"{tid}_{req_task_id}"
                prep_reqs.append((req_id, tid, req_task_id, status_str))
                
        # Prepare required items
        prep_items = []
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
                        prep_items.append((f"{tid}_{iid}_handover", tid, iid, get_item_name(iid), count, fir, "Handover", dogtag_lvl))
            elif otype == "plantItem":
                for it in items:
                    iid = it.get("id")
                    if iid:
                        prep_items.append((f"{tid}_{iid}_required", tid, iid, get_item_name(iid), count, fir, "Required", dogtag_lvl))
            elif m_item:
                iid = m_item.get("id")
                if iid:
                    prep_items.append((f"{tid}_{iid}_marker", tid, iid, get_item_name(iid, "MS2000 Marker"), count, 0, "Required", None))
                    
        prepared_tasks.append({
            "id": tid,
            "name": tname,
            "trader": can_trader,
            "map": can_map,
            "level": can_level,
            "karma": can_karma,
            "kappa": can_kappa,
            "wiki": can_wiki,
            "objs": prep_objs,
            "reqs": prep_reqs,
            "items": prep_items
        })
        
    # 2. Apply batch to each database in a single transaction
    sample_modifications = []
    
    for db_idx, db_path in enumerate(active_dbs):
        conn = sqlite3.connect(db_path)
        cur = conn.cursor()
        
        # Check tables
        cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
        tables = {r[0] for r in cur.fetchall()}
        has_quests = "Quests" in tables
        has_objs = "QuestObjectives" in tables
        has_reqs = "QuestRequirements" in tables
        has_items = "QuestRequiredItems" in tables
        
        has_customized = False
        if has_objs:
            cur.execute("PRAGMA table_info(QuestObjectives)")
            has_customized = 'IsCustomized' in [c[1] for c in cur.fetchall()]
            
        cur.execute("BEGIN TRANSACTION")
        
        for ptask in prepared_tasks:
            tid = ptask["id"]
            
            # Quests
            if has_quests:
                cur.execute("SELECT Trader, Location, MinLevel, KappaRequired FROM Quests WHERE Id = ?", (tid,))
                q_row = cur.fetchone()
                if q_row:
                    ex_trader, ex_loc, ex_lvl, ex_kappa = q_row
                    final_lvl = ex_lvl if (ex_lvl and ex_lvl > 0 and (not ptask["level"] or ptask["level"] == 0)) else ptask["level"]
                    final_loc = ex_loc if (ex_loc and ',' in ex_loc and (not ptask["map"] or ptask["map"] == 'Any')) else ptask["map"]
                    if ex_trader != ptask["trader"] or ex_loc != final_loc or ex_lvl != final_lvl or ex_kappa != ptask["kappa"]:
                        cur.execute("""
                            UPDATE Quests
                            SET Trader = ?, Location = ?, MinLevel = ?, KappaRequired = ?, WikiPageLink = ?, UpdatedAt = datetime('now')
                            WHERE Id = ?
                        """, (ptask["trader"], final_loc, final_lvl, ptask["kappa"], ptask["wiki"], tid))
                        if db_idx == 0:
                            sample_modifications.append(f"[{ptask['name']}] Header actualizado: Trader='{ptask['trader']}', Map='{final_loc}', Lvl={final_lvl}")
                else:
                    cur.execute("""
                        INSERT INTO Quests (Id, Name, NameEN, Trader, Location, MinLevel, KappaRequired, WikiPageLink, UpdatedAt, IsApproved)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, datetime('now'), 1)
                    """, (tid, ptask["name"], ptask["name"], ptask["trader"], ptask["map"], ptask["level"], ptask["kappa"], ptask["wiki"]))
                    if db_idx == 0:
                        sample_modifications.append(f"[{ptask['name']}] Insertada misión nueva")
                        
            # QuestObjectives
            if has_objs:
                can_obj_ids = {o[0] for o in ptask["objs"]}
                cur.execute("SELECT Id, Description, IsCustomized, MapName, LocationPoints FROM QuestObjectives WHERE QuestId = ?", (tid,))
                existing_rows = cur.fetchall()
                existing = {r[0]: (r[1], r[2] if has_customized else 0, r[3], r[4]) for r in existing_rows}
                
                # Check if existing has rich polygon points or more objectives
                existing_has_polygons = any(r[4] and len(r[4].strip()) > 200 for r in existing_rows)
                
                # Delete stale ONLY IF not customized AND we are not degrading rich polygon coordinates
                if not (existing_has_polygons and len(ptask["objs"]) < len(existing)):
                    for ex_id, (ex_desc, ex_cust, ex_map, ex_pts) in existing.items():
                        if ex_id not in can_obj_ids and ex_cust != 1:
                            if ex_pts and len(ex_pts.strip()) > 50 and not any(o[7] for o in ptask["objs"]):
                                continue # Protect coordinates
                            cur.execute("DELETE FROM QuestObjectives WHERE Id = ?", (ex_id,))
                            if db_idx == 0:
                                sample_modifications.append(f"[{ptask['name']}] Objetivo obsoleto eliminado: {ex_desc}")
                            
                # Insert / update canonical
                for (oid, sort_idx, otype, odesc, ocount, ofir, omap, loc_points) in ptask["objs"]:
                    if oid in existing:
                        is_cust = existing[oid][1]
                        ex_map = existing[oid][2]
                        if ex_map != omap and db_idx == 0:
                            sample_modifications.append(f"[{ptask['name']}] Mapa de objetivo corregido: '{ex_map}' -> '{omap}' ({odesc[:45]}...)")
                        if is_cust == 1:
                            cur.execute("""
                                UPDATE QuestObjectives
                                SET SortOrder = ?, ObjectiveType = ?, Description = ?, TargetCount = ?, RequiresFIR = ?, MapName = ?, UpdatedAt = datetime('now')
                                WHERE Id = ?
                            """, (sort_idx, otype, odesc, ocount, ofir, omap, oid))
                        else:
                            if loc_points:
                                cur.execute("""
                                    UPDATE QuestObjectives
                                    SET SortOrder = ?, ObjectiveType = ?, Description = ?, TargetCount = ?, RequiresFIR = ?, MapName = ?, LocationPoints = ?, UpdatedAt = datetime('now')
                                    WHERE Id = ?
                                """, (sort_idx, otype, odesc, ocount, ofir, omap, loc_points, oid))
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
                            """, (oid, tid, sort_idx, otype, odesc, ocount, ofir, omap, loc_points))
                        else:
                            cur.execute("""
                                INSERT OR REPLACE INTO QuestObjectives (Id, QuestId, SortOrder, ObjectiveType, Description, TargetCount, RequiresFIR, MapName, LocationPoints, IsApproved, UpdatedAt)
                                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1, datetime('now'))
                            """, (oid, tid, sort_idx, otype, odesc, ocount, ofir, omap, loc_points))
                        if db_idx == 0:
                            sample_modifications.append(f"[{ptask['name']}] Objetivo canónico añadido: [{otype}] {odesc}")
                            
            # QuestRequirements
            if has_reqs:
                # Do NOT wipe existing requirements if incoming is empty and existing has requirements!
                cur.execute("SELECT COUNT(*) FROM QuestRequirements WHERE QuestId = ?", (tid,))
                ex_req_count = cur.fetchone()[0]
                if len(ptask["reqs"]) > 0 or ex_req_count == 0:
                    cur.execute("DELETE FROM QuestRequirements WHERE QuestId = ?", (tid,))
                    for (req_id, qid, req_qid, status_str) in ptask["reqs"]:
                        norm_status = 'Fail' if status_str.lower() in ('fail', 'failed') else ('Accept' if status_str.lower() in ('accept', 'active') else 'Complete')
                        cur.execute("""
                            INSERT OR IGNORE INTO QuestRequirements (Id, QuestId, RequiredQuestId, RequirementType, GroupId, IsApproved, UpdatedAt)
                            VALUES (?, ?, ?, ?, 0, 1, datetime('now'))
                        """, (req_id, qid, req_qid, norm_status))
                    
            # QuestRequiredItems
            if has_items and len(ptask["items"]) > 0:
                cur.execute("DELETE FROM QuestRequiredItems WHERE QuestId = ?", (tid,))
                for idx_item, (row_id, qid, iid, iname, icount, ifir, itype, idogtag) in enumerate(ptask["items"]):
                    cur.execute("""
                        INSERT OR REPLACE INTO QuestRequiredItems (Id, QuestId, ItemId, ItemName, Count, RequiresFIR, RequirementType, SortOrder, DogtagMinLevel, IsApproved, UpdatedAt)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1, datetime('now'))
                    """, (row_id, qid, iid, iname, icount, ifir, itype, idx_item, idogtag))
                    
        conn.commit()
        conn.close()
        
    print(f"Bloque {block_num} completado en {len(active_dbs)} bases de datos.", flush=True)
    print(f"Modificaciones destacadas en este bloque ({len(sample_modifications)} cambios):", flush=True)
    for m in sample_modifications[:12]:
        print(f"  * {m}", flush=True)
    if len(sample_modifications) > 12:
        print(f"  ... y {len(sample_modifications) - 12} correcciones adicionales.", flush=True)
        
    return {
        "block": block_num,
        "range": f"{start_idx + 1} - {actual_end}",
        "tasks": len(target_tasks),
        "changes_count": len(sample_modifications)
    }

def run_all_blocks():
    total_tasks = len(core_tasks)
    block_size = 50
    num_blocks = (total_tasks + block_size - 1) // block_size
    
    print(f"Iniciando auditoría y sincronización completa de {total_tasks} misiones en {num_blocks} bloques...", flush=True)
    summaries = []
    
    for b in range(num_blocks):
        start = b * block_size
        end = min((b + 1) * block_size, total_tasks)
        res = sync_single_block(b + 1, start, end)
        summaries.append(res)
        
    print(f"\n=======================================================", flush=True)
    print("RESUMEN FINAL DE LA AUDITORÍA DE TODAS LAS MISIONES:", flush=True)
    print(f"=======================================================", flush=True)
    total_modifications = sum(s["changes_count"] for s in summaries)
    for s in summaries:
        print(f"Bloque {s['block']} (Misiones {s['range']}): {s['changes_count']} correcciones aplicadas.")
    print(f"\nTotal correcciones aplicadas en todas las misiones: {total_modifications}", flush=True)

if __name__ == "__main__":
    if len(sys.argv) > 2:
        sync_single_block(1, int(sys.argv[1]), int(sys.argv[2]))
    else:
        run_all_blocks()
