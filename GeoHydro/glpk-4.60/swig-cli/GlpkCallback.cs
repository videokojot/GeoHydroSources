using System;
using System.Collections.Generic;

namespace org.gnu.glpk {

    /**
     * This class manages callbacks from the MIP solver.
     * <p>The GLPK MIP solver calls method {@link #callback(IntPtr) callback} in
     * the branch-and-cut algorithm. A listener to the callback can be used to
     * influence the sequence in which nodes of the search tree are evaluated,
     * or to supply a heuristic solution. To find out why the callback is
     * issued use method {@link GLPK#glp_ios_reason(glp_tree)
     * GLPK.glp_ios_reason}.
     */
    public class GlpkCallback {

        /**
         * List of callback listeners.
         */
        private static LinkedList<IGlpkCallbackListener> listeners
                = new LinkedList<IGlpkCallbackListener>();

        /**
         * Constructor.
         */
        private GlpkCallback() {
        }

        /**
         * Callback method called by native library.
         * @param cPtr pointer to search tree
         */
        public static void callback(IntPtr cPtr) {
            glp_tree tree;
            tree = new glp_tree(cPtr, false);
            foreach (IGlpkCallbackListener listener in listeners) {
                listener.callback(tree);
            }
        }

        /**
         * Adds a listener for callbacks.
         * @param listener listener for callbacks
         */
        public static void addListener(IGlpkCallbackListener listener) {
            listeners.AddLast(listener);
        }

        /**
         * Removes first occurance of a listener for callbacks.
         * @param listener listener for callbacks
         * @return true if the listener was found
         */
        public static bool removeListener(IGlpkCallbackListener listener) {
            if (listeners.Contains(listener)) {
                listeners.Remove(listener);
                return true;
            } else {
                return false;
            }
        }
    }
}
