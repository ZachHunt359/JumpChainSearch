// Cross-component communication setup for JumpChain Search
// Handles NavMenu <-> Index component interactions via custom events

window.setupIndexListener = function(dotNetRef) {
    window.indexRef = dotNetRef;
    
    if (!window.openDocumentListenerSet) {
        window.addEventListener('openDocument', async (e) => {
            try {
                await dotNetRef.invokeMethodAsync('OpenDocumentFromNav', parseInt(e.detail));
            } catch (error) {
                console.error('Error opening document from nav:', error);
            }
        });
        window.openDocumentListenerSet = true;
    }
};

window.setupNavMenuListener = function(dotNetRef) {
    window.navMenuRef = dotNetRef;
    
    if (!window.favoritesChangedListenerSet) {
        window.addEventListener('favoritesChanged', async (e) => {
            try {
                await dotNetRef.invokeMethodAsync('RefreshFavorites');
            } catch (error) {
                console.error('Error refreshing favorites:', error);
            }
        });
        window.favoritesChangedListenerSet = true;
    }
};

// Helper function to dispatch openDocument event
window.openDocumentModal = function(documentId) {
    window.dispatchEvent(new CustomEvent('openDocument', { 
        detail: documentId 
    }));
};
