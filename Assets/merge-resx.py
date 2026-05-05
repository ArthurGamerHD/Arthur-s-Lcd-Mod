#!/usr/bin/env python3

import argparse
import copy
import shutil
from pathlib import Path
import xml.etree.ElementTree as ET


def get_resx_keys(root):
    keys = set()

    for elem in root.findall("data"):
        name = elem.get("name")
        if name:
            keys.add(name)

    return keys


def get_first_data_items_by_key(root):
    items = {}

    for elem in root.findall("data"):
        name = elem.get("name")

        if name and name not in items:
            items[name] = elem

    return items


def remove_duplicate_data_items(root):
    """
    Removes duplicate <data name="..."> entries.

    The first occurrence is kept.
    Later duplicate entries are removed.
    """
    seen = set()
    removed = 0

    for elem in list(root.findall("data")):
        name = elem.get("name")

        if not name:
            continue

        if name in seen:
            root.remove(elem)
            removed += 1
        else:
            seen.add(name)

    return removed


def replace_child(root, old_child, new_child):
    children = list(root)
    index = children.index(old_child)

    root.remove(old_child)
    root.insert(index, new_child)


def elements_are_equal(a, b):
    return ET.tostring(a, encoding="utf-8") == ET.tostring(b, encoding="utf-8")


def merge_resx_file(
    source_file,
    target_file,
    dry_run=False,
    backup=False,
    source_priority=False
):
    source_tree = ET.parse(source_file)
    source_root = source_tree.getroot()

    target_tree = ET.parse(target_file)
    target_root = target_tree.getroot()

    duplicates_removed = remove_duplicate_data_items(target_root)

    source_items = get_first_data_items_by_key(source_root)
    target_items = get_first_data_items_by_key(target_root)

    added = 0
    replaced = 0

    for key, source_elem in source_items.items():
        target_elem = target_items.get(key)

        if target_elem is None:
            added += 1

            if not dry_run:
                new_elem = copy.deepcopy(source_elem)
                target_root.append(new_elem)
                target_items[key] = new_elem

        elif source_priority:
            if not elements_are_equal(source_elem, target_elem):
                replaced += 1

                if not dry_run:
                    new_elem = copy.deepcopy(source_elem)
                    replace_child(target_root, target_elem, new_elem)
                    target_items[key] = new_elem

    changed = added > 0 or replaced > 0 or duplicates_removed > 0

    if changed and not dry_run:
        if backup:
            backup_file = target_file.with_suffix(target_file.suffix + ".bak")
            shutil.copy2(target_file, backup_file)

        target_tree.write(
            target_file,
            encoding="utf-8",
            xml_declaration=True
        )

    return added, replaced, duplicates_removed


def main():
    parser = argparse.ArgumentParser(
        description=(
            "Merge missing .resx keys from one folder into another, "
            "optionally replacing target keys with source keys, "
            "and removing duplicate keys from target files."
        )
    )

    parser.add_argument(
        "source_folder",
        help="Folder containing the .resx files to copy keys from"
    )

    parser.add_argument(
        "target_folder",
        help="Folder containing the .resx files to modify"
    )

    parser.add_argument(
        "--source-priority",
        action="store_true",
        help="When a key exists in both source and target, replace the target entry with the source entry"
    )

    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show what would be changed without modifying files"
    )

    parser.add_argument(
        "--backup",
        action="store_true",
        help="Create .bak backup files before modifying target files"
    )

    parser.add_argument(
        "--create-missing-files",
        action="store_true",
        help="Copy source .resx files into target folder when the target file does not exist"
    )

    args = parser.parse_args()

    source_folder = Path(args.source_folder)
    target_folder = Path(args.target_folder)

    if not source_folder.is_dir():
        raise SystemExit(f"Source folder does not exist: {source_folder}")

    if not target_folder.is_dir():
        raise SystemExit(f"Target folder does not exist: {target_folder}")

    total_added = 0
    total_replaced = 0
    total_duplicates_removed = 0
    files_processed = 0

    for source_file in sorted(source_folder.glob("*.resx")):
        target_file = target_folder / source_file.name

        if not target_file.exists():
            if args.create_missing_files:
                print(f"Creating missing file: {target_file.name}")

                if not args.dry_run:
                    shutil.copy2(source_file, target_file)

                files_processed += 1
            else:
                print(f"Skipping, target missing: {target_file.name}")

            continue

        added, replaced, duplicates_removed = merge_resx_file(
            source_file,
            target_file,
            dry_run=args.dry_run,
            backup=args.backup,
            source_priority=args.source_priority
        )

        files_processed += 1
        total_added += added
        total_replaced += replaced
        total_duplicates_removed += duplicates_removed

        print(
            f"{target_file.name}: "
            f"added {added} missing keys, "
            f"replaced {replaced} existing keys, "
            f"removed {duplicates_removed} duplicate target keys"
        )

    print()
    print(f"Processed files: {files_processed}")
    print(f"Total keys added: {total_added}")
    print(f"Total keys replaced: {total_replaced}")
    print(f"Total duplicate target keys removed: {total_duplicates_removed}")

    if args.source_priority:
        print("Priority mode: source wins on duplicate keys.")
    else:
        print("Priority mode: target wins on duplicate keys.")

    if args.dry_run:
        print("Dry run only. No files were modified.")


if __name__ == "__main__":
    main()