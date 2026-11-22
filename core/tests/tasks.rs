//! Todo lists: columns, cards, subtasks, comments, and how they follow
//! their note through delete, trash, and duplicate.

use heracles_core::{CoreError, NewNote, NewTask, Note, NoteKind, Store, Task, TaskColumn, TaskPatch};
use tempfile::TempDir;

fn open() -> (TempDir, Store) {
    let dir = tempfile::tempdir().unwrap();
    let store = Store::open(dir.path()).unwrap();
    (dir, store)
}

fn note(store: &Store, kind: NoteKind, title: &str, parent: Option<&Note>) -> Note {
    store
        .create_note(NewNote {
            kind: Some(kind),
            title: Some(title.to_string()),
            parent_id: parent.map(|parent| parent.id.clone()),
            ..NewNote::default()
        })
        .unwrap()
}

/// A todo list with its three default columns.
fn board(store: &Store) -> (Note, Vec<TaskColumn>) {
    let list = note(store, NoteKind::TodoList, "Board", None);
    let columns = store.list_columns(&list.id).unwrap();
    (list, columns)
}

fn card(store: &Store, list: &Note, column: &TaskColumn, title: &str) -> Task {
    store
        .create_task(NewTask {
            todo_list_id: list.id.clone(),
            column_id: Some(column.id.clone()),
            title: Some(title.to_string()),
            ..NewTask::default()
        })
        .unwrap()
}

fn subtask(store: &Store, parent: &Task, title: &str) -> Task {
    store
        .create_task(NewTask {
            todo_list_id: parent.todo_list_id.clone(),
            parent_task_id: Some(parent.id.clone()),
            title: Some(title.to_string()),
            ..NewTask::default()
        })
        .unwrap()
}

/// Titles of the cards in a column, in order.
fn cards_in(store: &Store, list: &Note, column: &TaskColumn) -> Vec<String> {
    store
        .list_tasks(&list.id)
        .unwrap()
        .into_iter()
        .filter(|task| task.column_id.as_deref() == Some(column.id.as_str()))
        .map(|task| task.title)
        .collect()
}

fn subtasks_of(store: &Store, parent: &Task) -> Vec<String> {
    store
        .list_tasks(&parent.todo_list_id)
        .unwrap()
        .into_iter()
        .filter(|task| task.parent_task_id.as_deref() == Some(parent.id.as_str()))
        .map(|task| task.title)
        .collect()
}

fn column_names(store: &Store, list: &Note) -> Vec<String> {
    store.list_columns(&list.id).unwrap().into_iter().map(|column| column.name).collect()
}

// ─── Columns ────────────────────────────────────────────────────────────────

#[test]
fn a_new_todo_list_starts_with_three_columns() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    assert_eq!(column_names(&store, &list), vec!["Up Next", "Active", "Done"]);
    assert_eq!(columns.iter().map(|column| column.position).collect::<Vec<_>>(), vec![0, 1, 2]);
    assert!(columns.iter().all(|column| column.todo_list_id == list.id));
}

#[test]
fn only_todo_lists_have_columns() {
    let (_dir, store) = open();
    let page = note(&store, NoteKind::Page, "Page", None);
    assert!(store.list_columns(&page.id).unwrap().is_empty());
    assert!(matches!(store.create_column(&page.id, "x"), Err(CoreError::Invalid(_))));
    assert!(matches!(store.create_column("missing", "x"), Err(CoreError::NotFound { .. })));
}

#[test]
fn columns_are_added_at_the_end_and_blank_names_get_a_default() {
    let (_dir, store) = open();
    let (list, _) = board(&store);
    let added = store.create_column(&list.id, "  Review ").unwrap();
    let unnamed = store.create_column(&list.id, "  ").unwrap();
    assert_eq!((added.name.as_str(), added.position), ("Review", 3));
    assert_eq!((unnamed.name.as_str(), unnamed.position), ("Untitled", 4));
}

#[test]
fn renaming_a_column_trims_and_ignores_a_blank_name() {
    let (_dir, store) = open();
    let (_list, columns) = board(&store);
    assert_eq!(store.rename_column(&columns[0].id, " Backlog ").unwrap().name, "Backlog");
    assert_eq!(store.rename_column(&columns[0].id, "  ").unwrap().name, "Backlog");
    assert!(matches!(store.rename_column("missing", "x"), Err(CoreError::NotFound { .. })));
}

#[test]
fn reordering_columns_renumbers_and_keeps_unlisted_columns_after() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let (other_list, other_columns) = board(&store);

    let order = vec![columns[2].id.clone(), other_columns[0].id.clone(), columns[0].id.clone()];
    let reordered = store.reorder_columns(&list.id, &order).unwrap();

    assert_eq!(
        reordered.iter().map(|column| column.name.as_str()).collect::<Vec<_>>(),
        vec!["Done", "Up Next", "Active"]
    );
    assert_eq!(reordered.iter().map(|column| column.position).collect::<Vec<_>>(), vec![0, 1, 2]);
    assert_eq!(column_names(&store, &other_list), vec!["Up Next", "Active", "Done"]);
}

#[test]
fn deleting_a_column_moves_its_cards_to_the_end_of_the_first_remaining_column() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    card(&store, &list, &columns[0], "kept 1");
    card(&store, &list, &columns[0], "kept 2");
    card(&store, &list, &columns[1], "moved 1");
    let with_subtask = card(&store, &list, &columns[1], "moved 2");
    subtask(&store, &with_subtask, "sub");

    let fallback = store.delete_column(&columns[1].id).unwrap();

    assert_eq!(fallback, columns[0].id);
    assert_eq!(column_names(&store, &list), vec!["Up Next", "Done"]);
    assert_eq!(
        store.list_columns(&list.id).unwrap().iter().map(|column| column.position).collect::<Vec<_>>(),
        vec![0, 1]
    );
    assert_eq!(cards_in(&store, &list, &columns[0]), vec!["kept 1", "kept 2", "moved 1", "moved 2"]);
    let positions: Vec<i64> = store
        .list_tasks(&list.id)
        .unwrap()
        .into_iter()
        .filter(|task| task.column_id.is_some())
        .map(|task| task.position)
        .collect();
    assert_eq!(positions, vec![0, 1, 2, 3], "moved cards must not share positions");
    assert_eq!(subtasks_of(&store, &with_subtask), vec!["sub"]);
}

#[test]
fn deleting_the_only_column_replaces_it_with_up_next() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    store.delete_column(&columns[2].id).unwrap();
    store.delete_column(&columns[1].id).unwrap();
    card(&store, &list, &columns[0], "survivor");

    let fallback = store.delete_column(&columns[0].id).unwrap();

    let remaining = store.list_columns(&list.id).unwrap();
    assert_eq!(remaining.len(), 1);
    assert_eq!(remaining[0].id, fallback);
    assert_eq!(remaining[0].name, "Up Next");
    assert_ne!(fallback, columns[0].id);
    assert_eq!(cards_in(&store, &list, &remaining[0]), vec!["survivor"]);
}

// ─── Tasks ──────────────────────────────────────────────────────────────────

#[test]
fn cards_are_added_at_the_end_of_their_column_with_defaults() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let first = card(&store, &list, &columns[0], "first");
    let second = card(&store, &list, &columns[0], "second");
    let elsewhere = card(&store, &list, &columns[1], "elsewhere");

    assert_eq!((first.position, second.position, elsewhere.position), (0, 1, 0));
    assert_eq!(first.priority, 4);
    assert!(!first.completed);
    assert_eq!((first.description.as_str(), first.due_date.as_ref(), first.location.as_ref()), ("", None, None));
    assert!(first.labels.is_empty() && first.reminders.is_empty());
    assert_eq!(store.get_task(&first.id).unwrap(), Some(first));
    assert_eq!(store.get_task("missing").unwrap(), None);
}

#[test]
fn a_subtask_sits_under_its_parent_and_has_no_column() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let parent = card(&store, &list, &columns[0], "parent");
    let one = subtask(&store, &parent, "one");
    let two = subtask(&store, &parent, "two");
    let deep = subtask(&store, &one, "deep");

    assert_eq!(one.column_id, None);
    assert_eq!(one.parent_task_id, Some(parent.id.clone()));
    assert_eq!((one.position, two.position, deep.position), (0, 1, 0));
    assert_eq!(subtasks_of(&store, &parent), vec!["one", "two"]);
}

#[test]
fn a_subtask_ignores_a_column_given_alongside_its_parent() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let parent = card(&store, &list, &columns[0], "parent");
    let sub = store
        .create_task(NewTask {
            todo_list_id: list.id.clone(),
            parent_task_id: Some(parent.id.clone()),
            column_id: Some(columns[1].id.clone()),
            ..NewTask::default()
        })
        .unwrap();
    assert_eq!(sub.column_id, None);
}

#[test]
fn a_card_needs_a_column_so_it_can_never_be_stored_out_of_sight() {
    let (_dir, store) = open();
    let (list, _) = board(&store);
    let result = store.create_task(NewTask { todo_list_id: list.id.clone(), ..NewTask::default() });
    assert!(matches!(result, Err(CoreError::Invalid(_))));
    assert!(store.list_tasks(&list.id).unwrap().is_empty());
}

#[test]
fn tasks_cannot_reference_another_board() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let (other, other_columns) = board(&store);
    let foreign_parent = card(&store, &other, &other_columns[0], "foreign");

    let wrong_column = store.create_task(NewTask {
        todo_list_id: list.id.clone(),
        column_id: Some(other_columns[0].id.clone()),
        ..NewTask::default()
    });
    let wrong_parent = store.create_task(NewTask {
        todo_list_id: list.id.clone(),
        parent_task_id: Some(foreign_parent.id.clone()),
        ..NewTask::default()
    });
    assert!(matches!(wrong_column, Err(CoreError::Invalid(_))));
    assert!(matches!(wrong_parent, Err(CoreError::Invalid(_))));

    let mine = card(&store, &list, &columns[0], "mine");
    assert!(matches!(store.move_task(&mine.id, Some(&other_columns[0].id), None, None), Err(CoreError::Invalid(_))));
    assert!(matches!(store.move_task(&mine.id, None, Some(&foreign_parent.id), None), Err(CoreError::Invalid(_))));
}

#[test]
fn updating_a_task_changes_only_the_fields_given() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let task = card(&store, &list, &columns[0], "task");

    let updated = store
        .update_task(
            &task.id,
            TaskPatch {
                description: Some("details".into()),
                completed: Some(true),
                due_date: Some(Some("2026-12-01".into())),
                deadline: Some(Some("2026-12-24".into())),
                priority: Some(1),
                labels: Some(vec!["home".into(), "urgent".into()]),
                reminders: Some(vec!["2026-11-30T09:00:00.000Z".into()]),
                location: Some(Some("Garage".into())),
                ..TaskPatch::default()
            },
        )
        .unwrap();
    assert_eq!(updated.title, "task");
    assert_eq!(updated.description, "details");
    assert!(updated.completed);
    assert_eq!(updated.due_date.as_deref(), Some("2026-12-01"));
    assert_eq!(updated.deadline.as_deref(), Some("2026-12-24"));
    assert_eq!(updated.priority, 1);
    assert_eq!(updated.labels, vec!["home", "urgent"]);
    assert_eq!(updated.reminders, vec!["2026-11-30T09:00:00.000Z"]);
    assert_eq!(updated.location.as_deref(), Some("Garage"));

    let cleared = store
        .update_task(
            &task.id,
            TaskPatch { title: Some("renamed".into()), due_date: Some(None), location: Some(None), ..TaskPatch::default() },
        )
        .unwrap();
    assert_eq!(cleared.title, "renamed");
    assert_eq!((cleared.due_date, cleared.location), (None, None));
    assert_eq!(cleared.deadline.as_deref(), Some("2026-12-24"), "fields left out are untouched");
    assert_eq!(cleared.labels, vec!["home", "urgent"]);
    assert!(cleared.completed);
}

#[test]
fn priority_must_be_between_one_and_four() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let task = card(&store, &list, &columns[0], "task");
    for bad in [0, 5] {
        let patched = store.update_task(&task.id, TaskPatch { priority: Some(bad), ..TaskPatch::default() });
        assert!(matches!(patched, Err(CoreError::Invalid(_))));
        let created = store.create_task(NewTask {
            todo_list_id: list.id.clone(),
            column_id: Some(columns[0].id.clone()),
            priority: Some(bad),
            ..NewTask::default()
        });
        assert!(matches!(created, Err(CoreError::Invalid(_))));
    }
    assert_eq!(store.get_task(&task.id).unwrap().unwrap().priority, 4);
}

#[test]
fn moving_a_card_between_columns_renumbers_the_destination() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let a = card(&store, &list, &columns[0], "a");
    let b = card(&store, &list, &columns[0], "b");
    card(&store, &list, &columns[1], "x");
    let y = card(&store, &list, &columns[1], "y");

    let moved = store.move_task(&a.id, Some(&columns[1].id), None, Some(&y.id)).unwrap();
    assert_eq!(moved.column_id, Some(columns[1].id.clone()));
    assert_eq!(cards_in(&store, &list, &columns[1]), vec!["x", "a", "y"]);
    assert_eq!(cards_in(&store, &list, &columns[0]), vec!["b"]);

    // Reorder within a column, and append when `before` is unknown.
    store.move_task(&y.id, Some(&columns[1].id), None, Some(&a.id)).unwrap();
    assert_eq!(cards_in(&store, &list, &columns[1]), vec!["x", "y", "a"]);
    store.move_task(&b.id, Some(&columns[1].id), None, Some("missing")).unwrap();
    assert_eq!(cards_in(&store, &list, &columns[1]), vec!["x", "y", "a", "b"]);
}

#[test]
fn a_subtask_can_be_promoted_to_a_card_and_a_card_put_under_another() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let parent = card(&store, &list, &columns[0], "parent");
    let sub = subtask(&store, &parent, "sub");
    let loner = card(&store, &list, &columns[1], "loner");

    let promoted = store.move_task(&sub.id, Some(&columns[2].id), None, None).unwrap();
    assert_eq!((promoted.parent_task_id.clone(), promoted.column_id.clone()), (None, Some(columns[2].id.clone())));

    let demoted = store.move_task(&loner.id, None, Some(&parent.id), None).unwrap();
    assert_eq!((demoted.parent_task_id.clone(), demoted.column_id.clone()), (Some(parent.id.clone()), None));
    assert_eq!(subtasks_of(&store, &parent), vec!["loner"]);
    assert!(cards_in(&store, &list, &columns[1]).is_empty());
}

#[test]
fn moving_a_task_under_itself_or_its_own_subtask_changes_nothing() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let parent = card(&store, &list, &columns[0], "parent");
    let child = subtask(&store, &parent, "child");
    let grandchild = subtask(&store, &child, "grandchild");

    for target in [&parent, &child, &grandchild] {
        let result = store.move_task(&parent.id, None, Some(&target.id), None).unwrap();
        assert_eq!(result.parent_task_id, None);
        assert_eq!(result.column_id, Some(columns[0].id.clone()));
    }
}

#[test]
fn a_move_must_name_a_column_or_a_parent() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let task = card(&store, &list, &columns[0], "task");
    assert!(matches!(store.move_task(&task.id, None, None, None), Err(CoreError::Invalid(_))));
    assert_eq!(store.get_task(&task.id).unwrap().unwrap().column_id, Some(columns[0].id.clone()));
}

#[test]
fn deleting_a_task_removes_its_subtasks_and_comments() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let parent = card(&store, &list, &columns[0], "parent");
    let child = subtask(&store, &parent, "child");
    let grandchild = subtask(&store, &child, "grandchild");
    let bystander = card(&store, &list, &columns[0], "bystander");
    store.create_comment(&grandchild.id, "note").unwrap();

    let mut deleted = store.delete_task(&parent.id).unwrap();
    deleted.sort();
    let mut expected = vec![parent.id.clone(), child.id.clone(), grandchild.id.clone()];
    expected.sort();
    assert_eq!(deleted, expected);
    assert_eq!(store.list_tasks(&list.id).unwrap(), vec![store.get_task(&bystander.id).unwrap().unwrap()]);
    assert!(store.list_comments(&grandchild.id).unwrap().is_empty());
    assert!(matches!(store.delete_task(&parent.id), Err(CoreError::NotFound { .. })));
}

// ─── Comments ───────────────────────────────────────────────────────────────

#[test]
fn comments_are_listed_oldest_first_and_can_be_edited_and_deleted() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let task = card(&store, &list, &columns[0], "task");

    let first = store.create_comment(&task.id, "first").unwrap();
    std::thread::sleep(std::time::Duration::from_millis(3));
    let second = store.create_comment(&task.id, "second").unwrap();
    assert_eq!(first.task_id, task.id);

    let edited = store.update_comment(&first.id, "first, edited").unwrap();
    assert_eq!(edited.body, "first, edited");
    let bodies: Vec<String> =
        store.list_comments(&task.id).unwrap().into_iter().map(|comment| comment.body).collect();
    assert_eq!(bodies, vec!["first, edited", "second"]);

    store.delete_comment(&second.id).unwrap();
    assert_eq!(store.list_comments(&task.id).unwrap().len(), 1);
    assert!(matches!(store.create_comment("missing", "x"), Err(CoreError::NotFound { .. })));
    assert!(matches!(store.update_comment("missing", "x"), Err(CoreError::NotFound { .. })));
    assert!(matches!(store.delete_comment("missing"), Err(CoreError::NotFound { .. })));
}

// ─── Boards follow their notes ──────────────────────────────────────────────

/// A page holding a todo list with one card, a subtask, and a comment.
fn page_with_board(store: &Store) -> (Note, Note, Task, Task) {
    let page = note(store, NoteKind::Page, "Page", None);
    let list = note(store, NoteKind::TodoList, "List", Some(&page));
    let columns = store.list_columns(&list.id).unwrap();
    let task = card(store, &list, &columns[0], "task");
    let sub = subtask(store, &task, "sub");
    store.create_comment(&task.id, "hello").unwrap();
    (page, list, task, sub)
}

#[test]
fn deleting_a_note_removes_the_boards_beneath_it() {
    let (_dir, store) = open();
    let (page, list, task, sub) = page_with_board(&store);

    store.delete_note(&page.id).unwrap();

    assert!(store.list_columns(&list.id).unwrap().is_empty());
    assert!(store.list_tasks(&list.id).unwrap().is_empty());
    assert_eq!(store.get_task(&sub.id).unwrap(), None);
    assert!(store.list_comments(&task.id).unwrap().is_empty());
}

#[test]
fn emptying_the_trash_removes_the_boards_of_trashed_notes() {
    let (_dir, store) = open();
    let (page, list, task, _sub) = page_with_board(&store);

    store.trash_note(&page.id).unwrap();
    assert_eq!(store.list_tasks(&list.id).unwrap().len(), 2, "trashing alone keeps the board");
    store.empty_trash().unwrap();

    assert!(store.list_columns(&list.id).unwrap().is_empty());
    assert!(store.list_tasks(&list.id).unwrap().is_empty());
    assert!(store.list_comments(&task.id).unwrap().is_empty());
}

#[test]
fn duplicating_a_todo_list_copies_columns_and_tasks_but_not_comments() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    store.rename_column(&columns[1].id, "Doing").unwrap();
    let extra = store.create_column(&list.id, "Review").unwrap();
    let task = card(&store, &list, &columns[1], "task");
    store
        .update_task(
            &task.id,
            TaskPatch {
                description: Some("details".into()),
                completed: Some(true),
                due_date: Some(Some("2026-12-01".into())),
                priority: Some(2),
                labels: Some(vec!["home".into()]),
                reminders: Some(vec!["2026-11-30T09:00:00.000Z".into()]),
                location: Some(Some("Garage".into())),
                ..TaskPatch::default()
            },
        )
        .unwrap();
    let sub = subtask(&store, &task, "sub");
    subtask(&store, &sub, "deep");
    card(&store, &list, &extra, "second");
    store.create_comment(&task.id, "not copied").unwrap();

    let copy = store.duplicate_note(&list.id).unwrap();

    assert_eq!(copy.title, "Board (copy)");
    assert_eq!(column_names(&store, &copy), vec!["Up Next", "Doing", "Done", "Review"]);
    let copied = store.list_tasks(&copy.id).unwrap();
    let original = store.list_tasks(&list.id).unwrap();
    assert_eq!(copied.len(), 4);
    assert_eq!(original.len(), 4, "the original board is untouched");
    assert!(copied.iter().all(|copy| original.iter().all(|task| task.id != copy.id)));

    let by_title = |title: &str| copied.iter().find(|task| task.title == title).unwrap();
    let copy_columns = store.list_columns(&copy.id).unwrap();
    let copied_task = by_title("task");
    assert_eq!(copied_task.column_id, Some(copy_columns[1].id.clone()));
    assert_eq!(copied_task.description, "details");
    assert!(copied_task.completed);
    assert_eq!(copied_task.due_date.as_deref(), Some("2026-12-01"));
    assert_eq!(copied_task.priority, 2);
    assert_eq!(copied_task.labels, vec!["home"]);
    assert_eq!(copied_task.reminders, vec!["2026-11-30T09:00:00.000Z"]);
    assert_eq!(copied_task.location.as_deref(), Some("Garage"));
    assert_eq!(by_title("sub").parent_task_id, Some(copied_task.id.clone()));
    assert_eq!(by_title("sub").column_id, None);
    assert_eq!(by_title("deep").parent_task_id, Some(by_title("sub").id.clone()));
    assert_eq!(by_title("second").column_id, Some(copy_columns[3].id.clone()));
    assert!(store.list_comments(&copied_task.id).unwrap().is_empty());
}

// ─── Reminders ──────────────────────────────────────────────────────────────

#[test]
fn outstanding_reminders_skip_completed_tasks_and_trashed_lists() {
    let (_dir, store) = open();
    let (list, columns) = board(&store);
    let remind = |task: &Task, times: &[&str]| {
        let reminders = Some(times.iter().map(|time| time.to_string()).collect());
        store.update_task(&task.id, TaskPatch { reminders, ..TaskPatch::default() }).unwrap();
    };
    let later = card(&store, &list, &columns[0], "later");
    remind(&later, &["2026-12-02T09:00:00.000Z", "2026-12-01T09:00:00.000Z"]);
    let sooner = card(&store, &list, &columns[0], "sooner");
    remind(&sooner, &["2026-11-30T09:00:00.000Z"]);
    let done = card(&store, &list, &columns[0], "done");
    remind(&done, &["2026-11-29T09:00:00.000Z"]);
    store.update_task(&done.id, TaskPatch { completed: Some(true), ..TaskPatch::default() }).unwrap();

    let (trashed_list, trashed_columns) = board(&store);
    let hidden = card(&store, &trashed_list, &trashed_columns[0], "hidden");
    remind(&hidden, &["2026-11-28T09:00:00.000Z"]);
    store.trash_note(&trashed_list.id).unwrap();

    let reminders = store.list_reminders().unwrap();
    let summary: Vec<(&str, &str)> = reminders
        .iter()
        .map(|reminder| (reminder.task_title.as_str(), reminder.remind_at.as_str()))
        .collect();
    assert_eq!(
        summary,
        vec![
            ("sooner", "2026-11-30T09:00:00.000Z"),
            ("later", "2026-12-01T09:00:00.000Z"),
            ("later", "2026-12-02T09:00:00.000Z"),
        ]
    );
    assert_eq!(reminders[0].task_id, sooner.id);
    assert_eq!(reminders[0].todo_list_id, list.id);
}
