import os
import base64
import sqlite3
import shutil
import urllib.parse

def migrate_icons():
    db_path = 'publish/tarkov_data.db'
    if not os.path.exists(db_path):
        db_path = 'TarkovHelper/Assets/tarkov_data.db'
        
    conn = sqlite3.connect(db_path)
    c = conn.cursor()
    c.execute("SELECT Id, Name, WikiPageLink FROM Items WHERE WikiPageLink IS NOT NULL AND WikiPageLink != '';")
    items = c.fetchall()
    
    print(f"Loaded {len(items)} items from {db_path}")
    
    # Dogtag mappings
    dogtag_ids = {
        '59f32bb586f774757e1e8442': 'dogtag-bear.png', # BEAR Dogtag
        '59f32c3b86f77472a31742f0': 'dogtag-usec.png', # USEC Dogtag
    }

    target_dirs = [
        'TarkovHelper/Assets/icons',
        'publish/Assets/icons'
    ]
    
    for icons_dir in target_dirs:
        if not os.path.exists(icons_dir):
            print(f"Directory {icons_dir} does not exist, skipping.")
            continue
            
        print(f"\nProcessing {icons_dir}...")
        files = set(os.listdir(icons_dir))
        renamed_count = 0
        copied_count = 0
        already_count = 0
        
        # 1. First process dogtags
        for dt_id, dt_file in dogtag_ids.items():
            src = os.path.join(icons_dir, dt_file)
            dst = os.path.join(icons_dir, f"{dt_id}.png")
            if os.path.exists(src) and not os.path.exists(dst):
                shutil.copyfile(src, dst)
                copied_count += 1
                files.add(f"{dt_id}.png")
                
        # 2. Process all items
        for item_id, name, wiki_link in items:
            dest_file = f"{item_id}.png"
            if dest_file in files:
                already_count += 1
                continue
                
            # Try to find matching base64 file
            found_src = None
            urls_to_try = [wiki_link, urllib.parse.unquote(wiki_link)]
            for u in urls_to_try:
                for b64 in [
                    base64.b64encode(u.encode('utf-8')).decode('utf-8'),
                    base64.urlsafe_b64encode(u.encode('utf-8')).decode('utf-8'),
                    base64.urlsafe_b64encode(u.encode('utf-8')).decode('utf-8').rstrip('='),
                    base64.b64encode(u.encode('utf-8')).decode('utf-8').rstrip('=')
                ]:
                    cand = f"{b64}.png"
                    if cand in files:
                        found_src = cand
                        break
                if found_src:
                    break
                    
            if found_src:
                src_path = os.path.join(icons_dir, found_src)
                dst_path = os.path.join(icons_dir, dest_file)
                # Copy instead of rename so any duplicate or legacy references still work
                shutil.copyfile(src_path, dst_path)
                # Now remove the old base64 file if it's no longer needed
                try:
                    os.remove(src_path)
                except:
                    pass
                files.remove(found_src)
                files.add(dest_file)
                renamed_count += 1

        print(f"Finished {icons_dir}:")
        print(f"  Renamed/Migrated: {renamed_count}")
        print(f"  Dogtags copied: {copied_count}")
        print(f"  Already existed by ID: {already_count}")

if __name__ == '__main__':
    migrate_icons()
