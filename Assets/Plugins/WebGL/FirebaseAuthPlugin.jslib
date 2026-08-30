// Bridges Unity C# to the real Firebase JS SDK (loaded dynamically from the
// same CDN the AI-Learning-Ecosystem web app uses) so login/register/Google
// sign-in and the users/loginLogs Firestore writes behave identically to that
// app - same SDK, same session handling, same field names. Firebase's own
// Unity SDK does not support the WebGL build target, which is why this goes
// through the browser's JS SDK instead of a native plugin.
mergeInto(LibraryManager.library, {

  $ale_firebase: {
    callbackObjectName: null,
    app: null,
    sdkLoadingPromise: null,

    loadScript: function (src) {
      return new Promise(function (resolve, reject) {
        var existing = document.querySelector('script[src="' + src + '"]');
        if (existing) { resolve(); return; }
        var s = document.createElement('script');
        s.src = src;
        s.onload = function () { resolve(); };
        s.onerror = function () { reject(new Error('Failed to load ' + src)); };
        document.head.appendChild(s);
      });
    },

    ensureSdkLoaded: function () {
      if (ale_firebase.sdkLoadingPromise) return ale_firebase.sdkLoadingPromise;
      var base = 'https://www.gstatic.com/firebasejs/10.12.2/';
      ale_firebase.sdkLoadingPromise = ale_firebase.loadScript(base + 'firebase-app-compat.js')
        .then(function () { return ale_firebase.loadScript(base + 'firebase-auth-compat.js'); })
        .then(function () { return ale_firebase.loadScript(base + 'firebase-firestore-compat.js'); });
      return ale_firebase.sdkLoadingPromise;
    },

    sendSuccess: function (user, provider) {
      var payload = JSON.stringify({ uid: user.uid, email: user.email || '', provider: provider });
      SendMessage(ale_firebase.callbackObjectName, 'OnAuthSuccess', payload);
    },

    sendError: function (err) {
      var message = (err && err.message) ? err.message : String(err);
      SendMessage(ale_firebase.callbackObjectName, 'OnAuthError', message);
    },

    // Mirrors authState.js's recordLoginEvent() field-for-field so login
    // history stays shared between this build and the web app. Failures are
    // swallowed (console warning only) exactly as the web app does - a
    // Firestore write hiccup should never block getting into the app.
    recordLoginEvent: function (user, provider) {
      try {
        var db = firebase.firestore();
        var ts = firebase.firestore.FieldValue.serverTimestamp();
        db.collection('users').doc(user.uid).set({
          email: user.email || null,
          provider: provider,
          lastLogin: ts
        }, { merge: true }).catch(function (e) {
          console.warn("Couldn't record login event in Firestore:", e.message);
        });
        db.collection('loginLogs').add({
          uid: user.uid,
          email: user.email || null,
          provider: provider,
          at: ts
        }).catch(function (e) {
          console.warn("Couldn't record login event in Firestore:", e.message);
        });
      } catch (e) {
        console.warn("Couldn't record login event in Firestore:", e.message);
      }
    }
  },

  FirebaseInit__deps: ['$ale_firebase'],
  FirebaseInit: function (configJsonPtr, callbackObjectNamePtr) {
    var configJson = UTF8ToString(configJsonPtr);
    ale_firebase.callbackObjectName = UTF8ToString(callbackObjectNamePtr);
    var config = JSON.parse(configJson);

    ale_firebase.ensureSdkLoaded().then(function () {
      if (!ale_firebase.app) {
        ale_firebase.app = firebase.initializeApp(config);
        firebase.auth().onAuthStateChanged(function (user) {
          if (user) {
            var payload = JSON.stringify({ uid: user.uid, email: user.email || '', provider: 'firebase' });
            SendMessage(ale_firebase.callbackObjectName, 'OnAuthStateChanged', payload);
          } else {
            SendMessage(ale_firebase.callbackObjectName, 'OnAuthStateChanged', '');
          }
        });
      }
      SendMessage(ale_firebase.callbackObjectName, 'OnFirebaseReady', '');
    }).catch(function (e) {
      ale_firebase.sendError(e);
    });
  },

  FirebaseSignIn__deps: ['$ale_firebase'],
  FirebaseSignIn: function (emailPtr, passwordPtr) {
    var email = UTF8ToString(emailPtr);
    var password = UTF8ToString(passwordPtr);
    firebase.auth().signInWithEmailAndPassword(email, password).then(function (credential) {
      ale_firebase.recordLoginEvent(credential.user, 'password');
      ale_firebase.sendSuccess(credential.user, 'password');
    }).catch(function (e) { ale_firebase.sendError(e); });
  },

  FirebaseRegister__deps: ['$ale_firebase'],
  FirebaseRegister: function (namePtr, emailPtr, passwordPtr) {
    var name = UTF8ToString(namePtr);
    var email = UTF8ToString(emailPtr);
    var password = UTF8ToString(passwordPtr);
    firebase.auth().createUserWithEmailAndPassword(email, password).then(function (credential) {
      var afterProfile = name
        ? credential.user.updateProfile({ displayName: name })
        : Promise.resolve();
      return afterProfile.then(function () {
        ale_firebase.recordLoginEvent(credential.user, 'password');
        ale_firebase.sendSuccess(credential.user, 'password');
      });
    }).catch(function (e) { ale_firebase.sendError(e); });
  },

  // Note: unlike the web app, this does not fall back to signInWithRedirect
  // on a blocked popup - a redirect navigates the whole page away and back,
  // which would tear down the running Unity WebGL instance. If the popup is
  // blocked, OnAuthError reports it so the UI can ask the player to allow
  // popups and try again.
  FirebaseGoogleSignIn__deps: ['$ale_firebase'],
  FirebaseGoogleSignIn: function () {
    var provider = new firebase.auth.GoogleAuthProvider();
    firebase.auth().signInWithPopup(provider).then(function (credential) {
      ale_firebase.recordLoginEvent(credential.user, 'google');
      ale_firebase.sendSuccess(credential.user, 'google');
    }).catch(function (e) {
      if (e && (e.code === 'auth/popup-closed-by-user' || e.code === 'auth/cancelled-popup-request')) {
        SendMessage(ale_firebase.callbackObjectName, 'OnAuthCancelled', '');
        return;
      }
      ale_firebase.sendError(e);
    });
  },

  FirebaseSignOut__deps: ['$ale_firebase'],
  FirebaseSignOut: function () {
    firebase.auth().signOut().catch(function (e) { ale_firebase.sendError(e); });
  }

});
