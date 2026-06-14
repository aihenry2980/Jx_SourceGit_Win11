# Jx SourceGit Win11 Homepage

This folder is a static Firebase Hosting site.

## Local files

- `index.html`: landing page
- `styles.css`: page styling
- `script.js`: page view and download click tracking
- `config.js`: fill in your Google Apps Script URL
- `config.example.js`: backup template for the analytics config
- `tools/google-sheets-analytics.gs`: Google Apps Script receiver for Google Sheets
- `firebase.json`: Firebase Hosting config when deploying from this folder

## Google Sheets tracking setup

1. Create a Google Sheet.
2. Open `Extensions > Apps Script`.
3. Paste the content of `tools/google-sheets-analytics.gs`.
4. Set `SPREADSHEET_ID` to your Google Sheet ID.
5. Deploy as `Web app`.
6. Set access to `Anyone`.
7. Copy the Web App URL.
8. Paste the URL into `config.js` as `googleAppsScriptUrl`.

The page sends:

- `page_view` when the page loads
- `download_click` when a release/download link is clicked

## Firebase deploy

From this folder:

```powershell
firebase.cmd deploy --only hosting
```

This folder pins the default Firebase project in `.firebaserc`:

```text
jx-sourcegit-win11
```

If your Firebase project is not selected yet:

```powershell
firebase.cmd use --add
firebase.cmd deploy --only hosting
```

You can also deploy with an explicit project id:

```powershell
firebase.cmd deploy --only hosting --project jx-sourcegit-win11
```
