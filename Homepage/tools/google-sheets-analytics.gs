// Use the spreadsheet ID from the Google Sheet URL, not the sheet file name.
// Example:
// https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMnOpQrStUvWxYz/edit
//                                      ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
const SPREADSHEET_ID = "PASTE_YOUR_GOOGLE_SHEET_ID_HERE";
const SHEET_NAME = "homepage_events";

function doPost(e) {
  const sheet = getSheet_();
  const payload = parsePayload_(e);

  sheet.appendRow([
    new Date(),
    payload.event || "",
    payload.project || "",
    payload.page || "",
    payload.title || "",
    payload.url || "",
    payload.targetUrl || "",
    payload.referrer || "",
    payload.language || "",
    payload.screen || "",
    payload.sessionId || "",
    payload.userAgent || "",
    payload.timestamp || ""
  ]);

  return ContentService
    .createTextOutput(JSON.stringify({ ok: true }))
    .setMimeType(ContentService.MimeType.JSON);
}

function doGet() {
  return ContentService
    .createTextOutput("Jx SourceGit analytics endpoint is running.")
    .setMimeType(ContentService.MimeType.TEXT);
}

function getSheet_() {
  if (!SPREADSHEET_ID || SPREADSHEET_ID === "PASTE_YOUR_GOOGLE_SHEET_ID_HERE") {
    throw new Error("Set SPREADSHEET_ID to the Google Sheet ID from the spreadsheet URL.");
  }

  const ss = SpreadsheetApp.openById(SPREADSHEET_ID);
  let sheet = ss.getSheetByName(SHEET_NAME);

  if (!sheet) {
    sheet = ss.insertSheet(SHEET_NAME);
  }

  if (sheet.getLastRow() === 0) {
    sheet.appendRow([
      "received_at",
      "event",
      "project",
      "page",
      "title",
      "url",
      "target_url",
      "referrer",
      "language",
      "screen",
      "session_id",
      "user_agent",
      "client_timestamp"
    ]);
  }

  return sheet;
}

function parsePayload_(e) {
  if (!e || !e.postData || !e.postData.contents) {
    return {};
  }

  try {
    return JSON.parse(e.postData.contents);
  } catch (err) {
    return {
      event: "parse_error",
      url: e.postData.contents
    };
  }
}
