// WARNING: Do not modify! Generated file.

namespace UnityEngine.Purchasing.Security {
    public class GooglePlayTangle
    {
        private static byte[] data = System.Convert.FromBase64String("pReUt6WYk5y/E90TYpiUlJSQlZa2HpjhNau51rvQEaauxFhB/0aLPBeUmpWlF5SflxeUlJVf33Zni62IleGmA+537dJJ+zATZPpVAXD+LLB8HWg9Z5QpT3kMVBboWqp7Fx6mdp1/0O0BFkPJqhunb7MNs33NogP6WGbF/ULFlRDyW3ktoMweNWBmnJssL+uHzrh2i7/NblHCQNufL5PhvyGB21b6YQBDSt+JJFX1AJ93VLwWUJ6CUfjfBRHIJ7N08exLKTWaNiRV4njW+Zviihut6sH2aL6kEMfkPcFP1LQCPq8rWOsGPdjGm1HZ5es1fCw1n2r80Dp0UsKQRrANW9pOU7G+e8ggx8C5MDT2cxDvVqEjbgk1kg+8uFHVEjxQpJeWlJWU");
        private static int[] order = new int[] { 0,8,8,3,8,13,9,7,13,11,10,12,12,13,14 };
        private static int key = 149;

        public static readonly bool IsPopulated = true;

        public static byte[] Data() {
        	if (IsPopulated == false)
        		return null;
            return Obfuscator.DeObfuscate(data, order, key);
        }
    }
}
