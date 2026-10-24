/* Lamha — one dialog for questions, in Lamha's look (confirm() shows the system's box, with OK / Cancel in the
 * system's language). Used by Settings (Firefox and the Windows app) and the Windows app's clipboard. Styles: .dlg in ui.css. */
var LamhaDialog = (() => {
  "use strict";

  /**
   * choices: [{ label, value, danger? }]. Resolves to the chosen value, or null for Esc / closing.
   * When one choice deletes something, the keyboard starts on a harmless one, so Enter can't delete by accident.
   */
  function ask(title, text, choices) {
    return new Promise(resolve => {
      const dlg = document.createElement("dialog");
      dlg.className = "dlg";
      dlg.setAttribute("aria-labelledby", "lamhaDlgT");
      const h2 = document.createElement("h2");
      h2.id = "lamhaDlgT";
      h2.textContent = title;
      dlg.append(h2);
      if (text) {
        const p = document.createElement("p");
        p.textContent = text;
        dlg.append(p);
      }
      const acts = document.createElement("div");
      acts.className = "dlg-acts";
      const safe = choices.some(c => c.danger) ? choices.findIndex(c => !c.danger) : 0;
      let settled = false;
      const done = value => {
        if (settled) return;
        settled = true;
        if (dlg.open && dlg.close) dlg.close();
        dlg.remove();
        resolve(value);
      };
      choices.forEach((c, i) => {
        const b = document.createElement("button");
        b.type = "button";
        b.className = "btn small" + (c.danger ? " danger-solid" : i ? " ghost" : "");
        b.textContent = c.label;
        if (i === safe) b.autofocus = true;
        b.addEventListener("click", () => done(c.value));
        acts.append(b);
      });
      dlg.append(acts);
      dlg.addEventListener("cancel", () => done(null));
      dlg.addEventListener("close", () => done(null));
      document.body.append(dlg);
      if (dlg.showModal) dlg.showModal();
      else dlg.setAttribute("open", ""); // engines without <dialog> (the tests' jsdom)
    });
  }

  /** A question before deleting something: true only when the user picks `yes`. */
  const confirmDelete = (title, yes, no, text = "") =>
    ask(title, text, [{ label: yes, value: true, danger: true }, { label: no, value: false }]).then(v => v === true);

  return { ask, confirmDelete };
})();
