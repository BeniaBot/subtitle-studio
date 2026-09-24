using System.Reflection;

// מה שווינדוס מציג על הקובץ: מנהל המשימות, ״מאפיינים ← פרטים״, וחלון ההרשאות.
// **עד 0.8.0 לא היה כאן כלום**, והתוכנה הופיעה שם רק בשם הקובץ.
//
// **הגרסה כפולה בכוונה ולא אוטומטית:** csc לא קורא קבוע מקוד אחר. היא חייבת
// להתאים ל-App.Version, ו-test-logic נכשל אם היא מפגרת.
[assembly: AssemblyTitle("Subtext")]
[assembly: AssemblyProduct("Subtext")]
[assembly: AssemblyDescription("אולפן הכתוביות - יצירה, תיקון והטמעה של כתוביות בעברית")]
[assembly: AssemblyCompany("BeniaBot")]
[assembly: AssemblyCopyright("MIT")]
[assembly: AssemblyVersion("0.8.1.0")]
[assembly: AssemblyFileVersion("0.8.1.0")]
