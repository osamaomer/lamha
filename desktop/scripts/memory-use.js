/* Lamha desktop — a minute of use before scripts/memory.ps1 -Use measures: the main window, the card for three
 * lookups, Settings opened and closed. Memory right after start leaves out what drawing those windows costs (the
 * graphics process grows with them). Started by main.js only from source, with LAMHA_PROFILE and LAMHA_MEMORY_USE=1. */
"use strict";

const sleep = ms => new Promise(r => setTimeout(r, ms));

module.exports = async ({ showMain, showCard, hideCard, openOptions, getOptionsWin, getMainWin }) => {
  await sleep(3000);
  showMain();
  await sleep(2000);
  for (const text of ["resilient", "bank", "tenacious"]) {
    await showCard("lookup", { text, hwnd: 0, terminal: true }); // no program to paste into: Replace stays hidden
    await sleep(2500);
    hideCard();
    await sleep(500);
  }
  openOptions();
  await sleep(3000);
  const options = getOptionsWin();
  if (options && !options.isDestroyed()) options.close();
  const main = getMainWin();
  if (main && !main.isDestroyed()) main.hide(); // back to the tray, where the app spends most of its time
  console.log("[memory] used");
};
